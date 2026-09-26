using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OtpNet;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// #495: <c>Accounts.CredentialsChangedAt</c> records when a password change, an owner's MFA reset
/// or an admin's MFA removal happened, in the transaction that makes the change, and a refresh
/// rotation of a token issued before it, or a personal-access-token mint whose re-authentication
/// started before it, is refused. Real repositories over a real relational schema. The access-JWT
/// check is in <c>JwtRequestAuthenticationShould</c>.
/// </summary>
public sealed class CredentialsChangedStampShould : IDisposable
{
    private static readonly string Password = TestPasswords.Valid;

    private readonly SqliteAuthDatabase _database = new();
    private readonly AccountRepository _accounts;
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();

    public CredentialsChangedStampShould()
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

    private async Task<DateTime?> StampAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.Accounts.Where(a => a.Id == id).Select(a => a.CredentialsChangedAt).SingleAsync();
    }

    private async Task StampAtAsync(AccountId id, DateTime at)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        await context.Accounts.Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CredentialsChangedAt, (DateTime?)at));
    }

    private AccountService AccountService() => new(NullLoggerFactory.Instance, _accounts,
        Substitute.For<IJwtUtils>(), Substitute.For<IMFAHashService>(), new MfaSetupRepository(_database),
        new DeviceRepository(_database), _cache, Substitute.For<ISecureRandom>(),
        new RefreshTokenService(new RefreshTokenRepository(_database), new SecureRandom(), TimeProvider.System),
        new DbTransactionRunner<AuthDbContext>(_database), new AuthenticationConfig(),
        TestLogin.Password(_accounts, _cache), TestLogin.Reauthentication(_accounts, _cache));

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

    // ---------------- The stamp is written with every change ----------------

    [Fact]
    public async Task Stamp_a_password_change()
    {
        Account account = await AccountAsync();
        DateTime before = DateTime.UtcNow;

        await AccountService().ChangePasswordAsync(account.Id, Password, TestPasswords.Other, IPAddress.Loopback);

        DateTime? stamp = await StampAsync(account.Id);
        Assert.NotNull(stamp);
        Assert.InRange(stamp!.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task Not_stamp_a_password_change_refused_for_a_wrong_current_password()
    {
        Account account = await AccountAsync();

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            AccountService().ChangePasswordAsync(account.Id, "wrong", TestPasswords.Other, IPAddress.Loopback));

        Assert.Null(await StampAsync(account.Id));
    }

    [Fact]
    public async Task Stamp_an_owners_mfa_reset()
    {
        Account account = await AccountAsync();
        string[] codes = await EnrolAsync(account);
        DateTime before = DateTime.UtcNow;

        Assert.True((await MfaService().ResetMFAAsync(account.Id, codes[0], codes[1], codes[2])).Success);

        DateTime? stamp = await StampAsync(account.Id);
        Assert.NotNull(stamp);
        Assert.InRange(stamp!.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task Not_stamp_an_mfa_reset_refused_for_wrong_recovery_codes()
    {
        Account account = await AccountAsync();
        await EnrolAsync(account);

        Assert.False((await MfaService().ResetMFAAsync(account.Id, "0000-0000-0000-0000", "x", "y")).Success);

        Assert.Null(await StampAsync(account.Id));
    }

    [Fact]
    public async Task Stamp_an_admins_mfa_removal()
    {
        Account admin = await AccountAsync("ADMINISTRATOR");
        Account account = await AccountAsync();
        await EnrolAsync(account);
        DateTime before = DateTime.UtcNow;

        Assert.True(await AccountService().RemoveMfaAsync(account.Id, admin.Id));

        DateTime? stamp = await StampAsync(account.Id);
        Assert.NotNull(stamp);
        Assert.InRange(stamp!.Value, before, DateTime.UtcNow);
        Assert.Null(await StampAsync(admin.Id));
    }

    [Fact]
    public async Task Still_report_an_unknown_account_to_an_admins_mfa_removal()
    {
        Assert.False(await AccountService().RemoveMfaAsync(new AccountId(999), new AccountId(1)));
    }

    // ---------------- Refresh rotation ----------------

    private RefreshTokenService Refresh() =>
        new(new RefreshTokenRepository(_database), new SecureRandom(), TimeProvider.System);

    private async Task<int> LiveRefreshTokensAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.RefreshTokens.CountAsync(t => t.AccountId == id && !t.Revoked);
    }

    [Fact]
    public async Task Refuse_to_rotate_a_refresh_token_issued_before_the_credentials_changed()
    {
        Account account = await AccountAsync();
        RefreshTokenService refresh = Refresh();
        RefreshIssueResult issued = await refresh.IssueAsync(account.Id);
        // The change is stamped after the token was issued, but the rotation read the token while
        // it was still live: the revocation the change made did not reach this read.
        await StampAtAsync(account.Id, DateTime.UtcNow.AddSeconds(1));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refresh.RotateAsync(issued.RawToken));

        Assert.Equal(1, await LiveRefreshTokensAsync(account.Id));
        await using AuthDbContext context = _database.CreateDbContext();
        Assert.Equal(1, await context.RefreshTokens.CountAsync(t => t.AccountId == account.Id));
    }

    [Fact]
    public async Task Rotate_a_refresh_token_issued_after_the_credentials_changed()
    {
        Account account = await AccountAsync();
        await StampAtAsync(account.Id, DateTime.UtcNow.AddSeconds(-1));
        RefreshTokenService refresh = Refresh();
        RefreshIssueResult issued = await refresh.IssueAsync(account.Id);

        RefreshRotateResult rotated = await refresh.RotateAsync(issued.RawToken);

        Assert.Equal(account.Id, rotated.AccountId);
        Assert.Equal(1, await LiveRefreshTokensAsync(account.Id));
    }

    // ---------------- Personal access token mint ----------------

    private PersonalAccessTokenService Pats() =>
        new(new PersonalAccessTokenRepository(_database), new SecureRandom(), TimeProvider.System);

    private async Task<int> TokensAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.PersonalAccessTokens.CountAsync(p => p.AccountId == id);
    }

    [Fact]
    public async Task Refuse_a_mint_whose_reauthentication_started_before_the_credentials_changed()
    {
        Account account = await AccountAsync();
        Reauthenticated proof = await TestLogin.Reauthentication(_accounts, _cache)
            .RequireCurrentPasswordAsync(account.Id, Password, IPAddress.Loopback);
        // A password change lands between the check and the mint.
        await StampAtAsync(account.Id, DateTime.UtcNow);

        AuthenticationException refused = await Assert.ThrowsAsync<AuthenticationException>(() =>
            Pats().MintSelfAsync(account.Id, AccountAccessLevel.Player, "cli", null, null, proof));

        Assert.Equal(PersonalAccessTokenService.CredentialsChanged, refused.Message);
        Assert.Equal(0, await TokensAsync(account.Id));
    }

    [Fact]
    public async Task Refuse_an_admin_mint_whose_reauthentication_started_before_the_admins_credentials_changed()
    {
        Account admin = await AccountAsync("ADMINISTRATOR");
        Account target = await AccountAsync();
        Reauthenticated proof = await TestLogin.Reauthentication(_accounts, _cache)
            .RequireCurrentPasswordAsync(admin.Id, Password, IPAddress.Loopback);
        await StampAtAsync(admin.Id, DateTime.UtcNow);

        await Assert.ThrowsAsync<AuthenticationException>(() => Pats().MintAdminAsync(
            AccountAccessLevel.Player | AccountAccessLevel.Admin, target.Id, "svc", null, AccountAccessLevel.Player,
            proof));

        Assert.Equal(0, await TokensAsync(target.Id));
    }

    [Fact]
    public async Task Mint_when_the_credentials_changed_before_the_reauthentication()
    {
        Account account = await AccountAsync();
        await StampAtAsync(account.Id, DateTime.UtcNow.AddSeconds(-1));
        Reauthenticated proof = await TestLogin.Reauthentication(_accounts, _cache)
            .RequireCurrentPasswordAsync(account.Id, Password, IPAddress.Loopback);

        MintResult minted = await Pats().MintSelfAsync(account.Id, AccountAccessLevel.Player, "cli", null, null, proof);

        Assert.StartsWith(PersonalAccessTokenService.TokenPrefix, minted.Token);
        Assert.Equal(1, await TokensAsync(account.Id));
    }
}
