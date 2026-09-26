using System.Net;
using System.Security.Authentication;
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
using Avalon.Network.Packets.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OtpNet;
using StackExchange.Redis;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// #483: a personal access token outlived a password change, and every token outlived the owner's
/// own MFA reset, so a token minted with a stolen session survived exactly the steps an owner
/// takes to get the account back. Both now revoke, in the transaction that makes the change, and
/// kick any world session. Against a real database, with tokens minted by the real service and
/// checked the way the token handler checks them.
/// </summary>
public sealed class CredentialRevocationShould : IDisposable
{
    private static readonly string Password = TestPasswords.Valid;

    private readonly SqliteAuthDatabase _database = new();
    private readonly AccountRepository _accounts;
    private readonly PersonalAccessTokenService _pats;
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();

    public CredentialRevocationShould()
    {
        _accounts = new AccountRepository(_database);
        _pats = new PersonalAccessTokenService(new PersonalAccessTokenRepository(_database), new SecureRandom(),
            TimeProvider.System);
    }

    public void Dispose() => _database.Dispose();

    private async Task<Account> AccountAsync()
    {
        string salt = BCrypt.Net.BCrypt.GenerateSalt(4);
        return await _accounts.CreateAsync(new Account
        {
            Username = "OWNER",
            Email = "owner@avalon.monster",
            Salt = Encoding.UTF8.GetBytes(salt),
            Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(Password, salt)),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        });
    }

    private async Task<string> MintPatAsync(AccountId id) =>
        (await _pats.MintSelfAsync(id, AccountAccessLevel.Player, "cli", null, null,
            new Reauthenticated(id, 0))).Token;

    private async Task RefreshTokenAsync(AccountId id) =>
        await new RefreshTokenRepository(_database).CreateAsync(new RefreshToken
        {
            AccountId = id,
            FamilyId = Guid.NewGuid(),
            Index = 0,
            Hash = [9, 9, 9],
            Revoked = false,
            Usages = 0,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });

    /// <summary>What the token handler refuses: a token that is missing or revoked.</summary>
    private async Task<bool> PatIsRefusedAsync(string token) =>
        await _pats.FindByRawTokenAsync(token) is null or { RevokedAt: not null };

    private async Task<bool> AllRefreshTokensRevokedAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.RefreshTokens.Where(t => t.AccountId == id).AllAsync(t => t.Revoked);
    }

    private AccountService AccountService() => new(NullLoggerFactory.Instance, _accounts,
        Substitute.For<IJwtUtils>(), Substitute.For<IMFAHashService>(), new MfaSetupRepository(_database),
        new DeviceRepository(_database), _cache, Substitute.For<ISecureRandom>(),
        new DbTransactionRunner<AuthDbContext>(_database), new AuthenticationConfig(),
        TestLogin.Password(_accounts, _cache), TestLogin.Reauthentication(_accounts, _cache));

    private MFAService MfaService() => new(NullLoggerFactory.Instance, new MfaSetupRepository(_database),
        Substitute.For<IMFAHashService>(), new SecureRandom(), _cache);

    private Task ChangePasswordAsync(AccountId id, string? current = null) =>
        AccountService().ChangePasswordAsync(id, current ?? Password, TestPasswords.Third, IPAddress.Loopback);

    [Fact]
    public async Task Refuse_a_personal_access_token_minted_before_a_password_change()
    {
        Account account = await AccountAsync();
        string pat = await MintPatAsync(account.Id);
        await RefreshTokenAsync(account.Id);

        await ChangePasswordAsync(account.Id);

        Assert.True(await PatIsRefusedAsync(pat));
        Assert.True(await AllRefreshTokensRevokedAsync(account.Id));
        await _cache.Received(1).PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, account.Id.Value.ToString());
        Account stored = (await _accounts.FindByIdAsync(account.Id))!;
        Assert.True(BCrypt.Net.BCrypt.Verify(TestPasswords.Third, Encoding.UTF8.GetString(stored.Verifier)));
    }

    [Fact]
    public async Task Keep_the_tokens_and_the_password_when_the_current_password_is_wrong()
    {
        Account account = await AccountAsync();
        string pat = await MintPatAsync(account.Id);

        await Assert.ThrowsAsync<AuthenticationException>(() => ChangePasswordAsync(account.Id, TestPasswords.Wrong));

        Assert.False(await PatIsRefusedAsync(pat));
        Account stored = (await _accounts.FindByIdAsync(account.Id))!;
        Assert.Equal(1, stored.FailedLogins);
        Assert.True(BCrypt.Net.BCrypt.Verify(Password, Encoding.UTF8.GetString(stored.Verifier)));
    }

    [Fact]
    public async Task Change_the_password_even_when_the_disconnect_publish_fails()
    {
        Account account = await AccountAsync();
        string pat = await MintPatAsync(account.Id);
        _cache.PublishAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        await ChangePasswordAsync(account.Id);

        Assert.True(await PatIsRefusedAsync(pat));
    }

    private async Task<string[]> EnrolAsync(Account account)
    {
        MFAService service = MfaService();
        Assert.True((await service.SetupMFAAsync(account, "Avalon")).Success);
        MFASetup pending = (await new MfaSetupRepository(_database).FindByAccountIdAsync(account.Id))!;
        MFAConfirmResult confirmed = await service.ConfirmMFAAsync(account.Id, 0, new Totp(pending.Secret).ComputeTotp());
        Assert.True(confirmed.Success);
        return confirmed.RecoveryCodes!;
    }

    [Fact]
    public async Task Refuse_every_token_issued_before_an_mfa_reset()
    {
        Account account = await AccountAsync();
        string[] codes = await EnrolAsync(account);
        string pat = await MintPatAsync(account.Id);
        await RefreshTokenAsync(account.Id);

        MFAResetResult reset = await MfaService().ResetMFAAsync(account.Id, 0, codes[0], codes[1], codes[2]);

        Assert.True(reset.Success);
        Assert.True(await PatIsRefusedAsync(pat));
        Assert.True(await AllRefreshTokensRevokedAsync(account.Id));
        await _cache.Received(1).PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, account.Id.Value.ToString());
    }

    [Fact]
    public async Task Keep_every_token_when_the_recovery_codes_are_wrong()
    {
        Account account = await AccountAsync();
        await EnrolAsync(account);
        string pat = await MintPatAsync(account.Id);

        MFAResetResult reset = await MfaService().ResetMFAAsync(account.Id, 0, "0000-0000-0000-0000", "x", "y");

        Assert.False(reset.Success);
        Assert.False(await PatIsRefusedAsync(pat));
        await _cache.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!);
    }

    [Fact]
    public async Task Reset_mfa_even_when_the_disconnect_publish_fails()
    {
        Account account = await AccountAsync();
        string[] codes = await EnrolAsync(account);
        string pat = await MintPatAsync(account.Id);
        _cache.PublishAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        MFAResetResult reset = await MfaService().ResetMFAAsync(account.Id, 0, codes[0], codes[1], codes[2]);

        Assert.True(reset.Success);
        Assert.True(await PatIsRefusedAsync(pat));
    }
}
