using System.Net;
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
using Avalon.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// An admin removing MFA from an account whose owner lost the authenticator, against a real
/// database through the real transaction runner. It is the only recovery path there is, so it has
/// to leave the account able to log in with its password and to enrol again, and it has to end
/// every session opened before the reset.
/// </summary>
public class AccountMfaRemovalShould : IDisposable
{
    private const string Password = "correct horse battery";
    private const string PendingHash = "PENDINGLOGINHASH";

    private readonly SqliteAuthDatabase _database = new();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly IDatabase _redis = Substitute.For<IDatabase>();
    private readonly IJwtUtils _jwt = Substitute.For<IJwtUtils>();
    private readonly AccountId _admin = new(1);

    public AccountMfaRemovalShould()
    {
        _cache.Database.Returns(_redis);
        _jwt.GenerateJwtToken(Arg.Any<Account>()).Returns("jwt");
    }

    [Fact]
    public async Task Let_the_account_log_in_with_its_password_alone_and_enrol_again()
    {
        Account account = await CreateAccountAsync("LOSTPHONE");
        await CreateConfirmedMfaAsync(account.Id);
        AccountService service = MakeService();

        (AuthenticateResponse before, _) = await service.Authenticate(Login("LOSTPHONE"), IPAddress.Loopback, default);
        Assert.Equal(AuthenticationResponseStatus.RequiresMFA, before.Status);

        Assert.True(await service.RemoveMfaAsync(account.Id, _admin));

        (AuthenticateResponse after, AccountId? loggedIn) =
            await service.Authenticate(Login("LOSTPHONE"), IPAddress.Loopback, default);
        Assert.Equal(AuthenticationResponseStatus.Success, after.Status);
        Assert.Equal(account.Id, loggedIn);

        bool enrolled = await new MfaSetupRepository(_database).UpsertPendingAsync(PendingSetup(account.Id));
        Assert.True(enrolled);
    }

    [Fact]
    public async Task Report_an_unknown_account_as_not_found()
    {
        Assert.False(await MakeService().RemoveMfaAsync(new AccountId(4242), _admin));
    }

    [Fact]
    public async Task Succeed_when_called_twice()
    {
        Account account = await CreateAccountAsync("TWICE");
        await CreateConfirmedMfaAsync(account.Id);
        AccountService service = MakeService();

        Assert.True(await service.RemoveMfaAsync(account.Id, _admin));
        Assert.True(await service.RemoveMfaAsync(account.Id, _admin));

        await using AuthDbContext context = _database.CreateDbContext();
        Assert.False(await context.MfaSetups.AnyAsync(m => m.AccountId == account.Id));
    }

    [Fact]
    public async Task Succeed_for_an_account_that_has_no_mfa()
    {
        Account account = await CreateAccountAsync("NOMFA");

        Assert.True(await MakeService().RemoveMfaAsync(account.Id, _admin));
    }

    [Fact]
    public async Task Leave_other_accounts_mfa_alone()
    {
        Account target = await CreateAccountAsync("TARGET");
        Account bystander = await CreateAccountAsync("BYSTANDER");
        await CreateConfirmedMfaAsync(target.Id);
        await CreateConfirmedMfaAsync(bystander.Id);

        await MakeService().RemoveMfaAsync(target.Id, _admin);

        await using AuthDbContext context = _database.CreateDbContext();
        Assert.True(await context.MfaSetups.AnyAsync(m => m.AccountId == bystander.Id));
    }

    [Fact]
    public async Task Revoke_the_accounts_refresh_tokens_and_personal_access_tokens()
    {
        Account account = await CreateAccountAsync("REVOKEME");
        await CreateConfirmedMfaAsync(account.Id);
        await new RefreshTokenRepository(_database).CreateAsync(new RefreshToken
        {
            AccountId = account.Id,
            FamilyId = Guid.NewGuid(),
            Index = 0,
            Hash = [9, 9, 9],
            Revoked = false,
            Usages = 0,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        await new PersonalAccessTokenRepository(_database).CreateAsync(new PersonalAccessToken
        {
            AccountId = account.Id,
            Name = "cli",
            TokenHash = [7, 7, 7],
            TokenPrefix = "avp_1234",
            Roles = Avalon.Common.Accounts.AccountAccessLevel.Player,
            CreatedAt = DateTime.UtcNow,
        });

        await MakeService().RemoveMfaAsync(account.Id, _admin);

        await using AuthDbContext context = _database.CreateDbContext();
        Assert.True(await context.RefreshTokens.Where(t => t.AccountId == account.Id).AllAsync(t => t.Revoked));
        Assert.True(await context.PersonalAccessTokens.Where(t => t.AccountId == account.Id)
            .AllAsync(t => t.RevokedAt != null && t.RevokedBy == _admin));
    }

    [Fact]
    public async Task Clear_the_pending_mfa_login_state_in_redis()
    {
        Account account = await CreateAccountAsync("MIDLOGIN");
        await CreateConfirmedMfaAsync(account.Id);
        _redis.HashGetAsync((RedisKey)CacheKeys.AccountMfa(account.Id.Value), (RedisValue)"hash", Arg.Any<CommandFlags>())
            .Returns((RedisValue)PendingHash);

        await MakeService().RemoveMfaAsync(account.Id, _admin);

        await _cache.Received(1).RemoveAsync(CacheKeys.AccountMfa(account.Id.Value));
        await _cache.Received(1).RemoveAsync(CacheKeys.MfaReverseHash(PendingHash));
    }

    private AccountService MakeService() => new(
        NullLoggerFactory.Instance,
        new AccountRepository(_database),
        _jwt,
        Substitute.For<IMFAHashService>(),
        new MfaSetupRepository(_database),
        new DeviceRepository(_database),
        _cache,
        Substitute.For<ISecureRandom>(),
        Substitute.For<IRefreshTokenService>(),
        new DbTransactionRunner<AuthDbContext>(_database),
        new AuthenticationConfig());

    private static AuthenticateRequest Login(string username) => new() { Username = username, Password = Password };

    private async Task<Account> CreateAccountAsync(string username)
    {
        string salt = BCrypt.Net.BCrypt.GenerateSalt();
        return await new AccountRepository(_database).CreateAsync(new Account
        {
            Username = username,
            Email = $"{username.ToLowerInvariant()}@avalon.monster",
            Salt = Encoding.UTF8.GetBytes(salt),
            Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(Password, salt)),
            SessionKey = [],
            LastIp = "127.0.0.1",
            LastAttemptIp = string.Empty,
            MuteBy = string.Empty,
            MuteReason = string.Empty,
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
        });
    }

    private async Task CreateConfirmedMfaAsync(AccountId accountId)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        context.MfaSetups.Add(new MFASetup
        {
            AccountId = accountId,
            Secret = [1, 2, 3],
            RecoveryCode1 = [4],
            RecoveryCode2 = [5],
            RecoveryCode3 = [6],
            Status = MfaSetupStatus.Confirmed,
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    private static MFASetup PendingSetup(AccountId accountId) => new()
    {
        AccountId = accountId,
        Secret = [8, 8, 8],
        RecoveryCode1 = [1],
        RecoveryCode2 = [2],
        RecoveryCode3 = [3],
        Status = MfaSetupStatus.Setup,
        CreatedAt = DateTime.UtcNow,
    };

    public void Dispose() => _database.Dispose();
}
