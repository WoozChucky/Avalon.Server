using System.Net;
using System.Security.Authentication;
using System.Text;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OtpNet;
using StackExchange.Redis;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// #495: <c>Accounts.CredentialsVersion</c> is raised by every password change, owner MFA reset and
/// admin MFA removal, in the transaction that makes the change. Whatever a proof of the credentials
/// issues carries the version of the very row the proof was checked against, and is refused once
/// the account has moved past it: a refresh-token family, its rotations, a personal access token,
/// an MFA hash. Real repositories over a real relational schema; the access-JWT check is in
/// <c>JwtRequestAuthenticationShould</c>, the TCP world key in the auth and world server tests.
/// </summary>
public sealed class CredentialsVersionShould : IDisposable
{
    private static readonly string Password = TestPasswords.Valid;
    private static readonly string NewPassword = TestPasswords.Other;

    private readonly SqliteAuthDatabase _database = new();
    private readonly AccountRepository _accounts;
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();

    public CredentialsVersionShould()
    {
        _accounts = new AccountRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    private async Task<Account> AccountAsync(string username = "OWNER")
    {
        string salt = BCrypt.Net.BCrypt.GenerateSalt(4);
        return await _accounts.CreateAsync(new Account
        {
            Username = username,
            Email = $"{username.ToLowerInvariant()}@avalon.monster",
            Salt = Encoding.UTF8.GetBytes(salt),
            Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(Password, salt)),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        });
    }

    private async Task<int> VersionAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.Accounts.Where(a => a.Id == id).Select(a => a.CredentialsVersion).SingleAsync();
    }

    private static readonly RefreshCaller Caller = RefreshCaller.From(IPAddress.Loopback, "test-agent");

    private RefreshTokenService Refresh() =>
        new(new RefreshTokenRepository(_database), new SecureRandom(), TimeProvider.System);

    private AccountService AccountService(IAccountRepository? accounts = null, IReauthentication? reauthentication = null) =>
        new(NullLoggerFactory.Instance,
        accounts ?? _accounts, Substitute.For<IJwtUtils>(), Substitute.For<IMFAHashService>(),
        new MfaSetupRepository(_database), new DeviceRepository(_database), _cache, Substitute.For<ISecureRandom>(),
        Refresh(), new DbTransactionRunner<AuthDbContext>(_database), new AuthenticationConfig(),
        TestLogin.Password(accounts ?? _accounts, _cache), reauthentication ?? TestLogin.Reauthentication(_accounts, _cache));

    private Task ChangePasswordAsync(AccountId id) =>
        AccountService().ChangePasswordAsync(id, Password, NewPassword, IPAddress.Loopback);

    private MFAService MfaService() => new(NullLoggerFactory.Instance, new MfaSetupRepository(_database),
        Substitute.For<IMFAHashService>(), new SecureRandom(), _cache);

    private async Task<string[]> EnrolAsync(Account account)
    {
        MFAService service = MfaService();
        Assert.True((await service.SetupMFAAsync(account, "Avalon")).Success);
        MFASetup pending = (await new MfaSetupRepository(_database).FindByAccountIdAsync(account.Id))!;
        MFAConfirmResult confirmed = await service.ConfirmMFAAsync(account.Id, new Totp(pending.Secret).ComputeTotp());
        Assert.True(confirmed.Success);
        return confirmed.RecoveryCodes!;
    }

    private async Task<int> LiveRefreshTokensAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.RefreshTokens.CountAsync(t => t.AccountId == id && !t.Revoked);
    }

    // ---------------- Every change raises the version ----------------

    [Fact]
    public async Task Raise_the_version_on_a_password_change()
    {
        Account account = await AccountAsync();

        await ChangePasswordAsync(account.Id);

        Assert.Equal(1, await VersionAsync(account.Id));
    }

    [Fact]
    public async Task Not_raise_the_version_when_the_current_password_is_wrong()
    {
        Account account = await AccountAsync();

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            AccountService().ChangePasswordAsync(account.Id, "wrong", NewPassword, IPAddress.Loopback));

        Assert.Equal(0, await VersionAsync(account.Id));
    }

    [Fact]
    public async Task Raise_the_version_on_an_owners_mfa_reset_and_not_on_a_refused_one()
    {
        Account account = await AccountAsync();
        string[] codes = await EnrolAsync(account);

        Assert.False((await MfaService().ResetMFAAsync(account.Id, "0000-0000-0000-0000", "x", "y")).Success);
        Assert.Equal(0, await VersionAsync(account.Id));

        Assert.True((await MfaService().ResetMFAAsync(account.Id, codes[0], codes[1], codes[2])).Success);
        Assert.Equal(1, await VersionAsync(account.Id));
    }

    [Fact]
    public async Task Raise_the_version_on_an_admins_mfa_removal_and_only_the_targets()
    {
        Account admin = await AccountAsync("ADMINISTRATOR");
        Account account = await AccountAsync();
        await EnrolAsync(account);

        Assert.True(await AccountService().RemoveMfaAsync(account.Id, admin.Id));
        Assert.False(await AccountService().RemoveMfaAsync(new AccountId(999), admin.Id));

        Assert.Equal(1, await VersionAsync(account.Id));
        Assert.Equal(0, await VersionAsync(admin.Id));
    }

    /// <summary>
    /// A login already past its password step holds an MFA hash made with the old password. The
    /// password change clears it, as an admin's MFA removal does.
    /// </summary>
    [Fact]
    public async Task Clear_the_pending_mfa_login_state_on_a_password_change()
    {
        Account account = await AccountAsync();
        IDatabase redis = Substitute.For<IDatabase>();
        _cache.Database.Returns(redis);
        redis.HashGetAsync((RedisKey)CacheKeys.AccountMfa(account.Id.Value), (RedisValue)"hash", Arg.Any<CommandFlags>())
            .Returns((RedisValue)"PENDINGHASH");

        await ChangePasswordAsync(account.Id);

        await _cache.Received(1).RemoveAsync(CacheKeys.MfaReverseHash("PENDINGHASH"));
        await _cache.Received(1).RemoveAsync(CacheKeys.AccountMfa(account.Id.Value));
    }

    /// <summary>
    /// #495 review: two password changes whose re-authentications both read the row at version 0.
    /// The write is a compare-and-set on that version, so only the first lands; the second is 401
    /// and its password is not written.
    /// </summary>
    [Fact]
    public async Task Let_only_the_change_at_the_current_version_win_two_racing_password_changes()
    {
        Account account = await AccountAsync();
        IReauthentication bothReadVersionZero = Substitute.For<IReauthentication>();
        bothReadVersionZero.RequireCurrentPasswordAsync(account.Id, Arg.Any<string>(), Arg.Any<IPAddress>(),
            Arg.Any<CancellationToken>()).Returns(new Reauthenticated(account.Id, 0));
        AccountService service = AccountService(reauthentication: bothReadVersionZero);

        await service.ChangePasswordAsync(account.Id, Password, TestPasswords.Third, IPAddress.Loopback);
        AuthenticationException refused = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.ChangePasswordAsync(account.Id, Password, TestPasswords.Fourth, IPAddress.Loopback));

        Assert.Equal(RefreshTokenService.CredentialsChanged, refused.Message);
        Assert.Equal(1, await VersionAsync(account.Id));
        Account stored = (await _accounts.FindByIdAsync(account.Id))!;
        Assert.True(BCrypt.Net.BCrypt.Verify(TestPasswords.Third, Encoding.UTF8.GetString(stored.Verifier)));
    }

    // ---------------- A login that proved the old password ----------------

    /// <summary>
    /// The login read the row, and its old verifier, before the change committed; the change's
    /// RevokeAll ran before the login went to open its refresh-token family. Timestamps compared
    /// the family's creation time with the change's and let it through. It is refused.
    /// </summary>
    [Fact]
    public async Task Refuse_a_refresh_family_for_a_login_that_proved_the_old_password()
    {
        Account account = await AccountAsync();
        IAccountRepository racing = Substitute.For<IAccountRepository>();
        racing.FindByUserNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            Account? read = await _accounts.FindByUserNameAsync(call.Arg<string>());
            await ChangePasswordAsync(account.Id);
            return read;
        });
        racing.TryRecordApiLoginAsync(Arg.Any<AccountId>(), Arg.Any<string>(), Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>()).Returns(true);

        (AuthenticateResponse response, AccountId? loggedIn, int version) = await AccountService(racing).Authenticate(
            new AuthenticateRequest { Username = "owner", Password = Password }, IPAddress.Loopback, default);
        Assert.Equal(AuthenticationResponseStatus.Success, response.Status);
        Assert.Equal(0, version);

        AuthenticationException refused = await Assert.ThrowsAsync<AuthenticationException>(() =>
            Refresh().IssueAsync(loggedIn!, version));

        Assert.Equal(RefreshTokenService.CredentialsChanged, refused.Message);
        Assert.Equal(0, await LiveRefreshTokensAsync(account.Id));
    }

    [Fact]
    public async Task Open_a_refresh_family_for_a_login_at_the_current_version()
    {
        Account account = await AccountAsync();
        await ChangePasswordAsync(account.Id);

        await Refresh().IssueAsync(account.Id, 1);

        Assert.Equal(1, await LiveRefreshTokensAsync(account.Id));
    }

    // ---------------- Refresh rotation ----------------

    [Fact]
    public async Task Refuse_to_rotate_a_token_of_a_family_opened_at_an_older_version()
    {
        Account account = await AccountAsync();
        RefreshTokenService refresh = Refresh();
        RefreshIssueResult issued = await refresh.IssueAsync(account.Id, 0);
        // The version moves while the token is still live: the rotation must not trust the read.
        await using (AuthDbContext context = _database.CreateDbContext())
            await AccountRepository.BumpCredentialsVersionAsync(context, account.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refresh.RotateAsync(issued.RawToken, Caller));

        await using AuthDbContext check = _database.CreateDbContext();
        Assert.Equal(1, await check.RefreshTokens.CountAsync(t => t.AccountId == account.Id));
    }

    [Fact]
    public async Task Rotate_a_token_at_the_current_version_and_carry_the_version_on()
    {
        Account account = await AccountAsync();
        RefreshTokenService refresh = Refresh();
        RefreshIssueResult issued = await refresh.IssueAsync(account.Id, 0);

        RefreshRotateResult rotated = await refresh.RotateAsync(issued.RawToken, Caller);

        Assert.Equal(0, rotated.CredentialsVersion);
        await using AuthDbContext context = _database.CreateDbContext();
        Assert.True(await context.RefreshTokens.Where(t => t.FamilyId == issued.FamilyId)
            .AllAsync(t => t.CredentialsVersion == 0));
    }

    // ---------------- Personal access token mint ----------------

    private PersonalAccessTokenService Pats() =>
        new(new PersonalAccessTokenRepository(_database), new SecureRandom(), TimeProvider.System);

    private async Task<int> TokensAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.PersonalAccessTokens.CountAsync(p => p.AccountId == id);
    }

    /// <summary>
    /// The re-authentication read the old password; the change committed before the mint. However
    /// the two interleave in time, the proof carries the version it read, and the mint is refused.
    /// </summary>
    [Fact]
    public async Task Refuse_a_mint_whose_reauthentication_read_the_old_password()
    {
        Account account = await AccountAsync();
        Reauthenticated proof = await TestLogin.Reauthentication(_accounts, _cache)
            .RequireCurrentPasswordAsync(account.Id, Password, IPAddress.Loopback);
        await ChangePasswordAsync(account.Id);

        AuthenticationException refused = await Assert.ThrowsAsync<AuthenticationException>(() =>
            Pats().MintSelfAsync(account.Id, AccountAccessLevel.Player, "cli", null, null, proof));

        Assert.Equal(PersonalAccessTokenService.CredentialsChanged, refused.Message);
        Assert.Equal(0, await TokensAsync(account.Id));
    }

    [Fact]
    public async Task Refuse_an_admin_mint_whose_reauthentication_read_the_admins_old_password()
    {
        Account admin = await AccountAsync("ADMINISTRATOR");
        Account target = await AccountAsync();
        Reauthenticated proof = await TestLogin.Reauthentication(_accounts, _cache)
            .RequireCurrentPasswordAsync(admin.Id, Password, IPAddress.Loopback);
        await ChangePasswordAsync(admin.Id);

        await Assert.ThrowsAsync<AuthenticationException>(() => Pats().MintAdminAsync(
            AccountAccessLevel.Player | AccountAccessLevel.Admin, target.Id, "svc", null, AccountAccessLevel.Player,
            proof));

        Assert.Equal(0, await TokensAsync(target.Id));
    }

    [Fact]
    public async Task Mint_on_a_reauthentication_with_the_new_password()
    {
        Account account = await AccountAsync();
        await ChangePasswordAsync(account.Id);
        Reauthenticated proof = await TestLogin.Reauthentication(_accounts, _cache)
            .RequireCurrentPasswordAsync(account.Id, NewPassword, IPAddress.Loopback);

        MintResult minted = await Pats().MintSelfAsync(account.Id, AccountAccessLevel.Player, "cli", null, null, proof);

        Assert.StartsWith(PersonalAccessTokenService.TokenPrefix, minted.Token);
        Assert.Equal(1, await TokensAsync(account.Id));
    }

    // ---------------- An MFA hash obtained with the old password ----------------

    [Fact]
    public async Task Refuse_to_complete_an_mfa_login_whose_hash_was_issued_before_the_change()
    {
        Account account = await AccountAsync();
        IMFAHashService hashes = Substitute.For<IMFAHashService>();
        hashes.GetAccountIdAsync("HASH").Returns(account.Id);
        hashes.RecordAttemptAsync(account.Id).Returns(1L);
        hashes.GetCredentialsVersionAsync(account.Id).Returns(0); // issued by the old password's login
        IMFAService mfa = Substitute.For<IMFAService>();
        mfa.VerifyMFAAsync("HASH", "123456", Arg.Any<CancellationToken>()).Returns(new MFAVerifyResult(true, account.Id));
        await ChangePasswordAsync(account.Id);

        MfaCodeAttempt attempt = await TestLogin.Mfa(_accounts, new CounterCacheless().Cache, mfa, hashes)
            .CheckAsync("HASH", "123456", LoginSource.FromAddress(IPAddress.Loopback), default);

        Assert.Equal(MfaCodeCheck.HashGone, attempt.Result);
        await hashes.Received(1).CleanupHash("HASH");
        await mfa.DidNotReceiveWithAnyArgs().VerifyMFAAsync(default!, default!, default);
    }

    /// <summary>A cache whose counters always answer a first attempt.</summary>
    private sealed class CounterCacheless
    {
        public IReplicatedCache Cache { get; } = Substitute.For<IReplicatedCache>();

        public CounterCacheless() =>
            Cache.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns(1L);
    }
}
