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
using Microsoft.Extensions.Logging;
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
    private static readonly string Password = TestPasswords.Valid;
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

        (AuthenticateResponse before, _, _) = await service.Authenticate(Login("LOSTPHONE"), IPAddress.Loopback, default);
        Assert.Equal(AuthenticationResponseStatus.RequiresMFA, before.Status);

        Assert.True(await service.RemoveMfaAsync(account.Id, _admin));

        (AuthenticateResponse after, AccountId? loggedIn, _) =
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

    [Fact]
    public async Task Publish_a_world_disconnect_for_the_account()
    {
        Account account = await CreateAccountAsync("KICKME");
        await CreateConfirmedMfaAsync(account.Id);

        await MakeService().RemoveMfaAsync(account.Id, _admin);

        await _cache.Received(1).PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, account.Id.Value.ToString());
    }

    [Fact]
    public async Task Not_publish_a_disconnect_for_an_unknown_account()
    {
        await MakeService().RemoveMfaAsync(new AccountId(4242), _admin);

        await _cache.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!);
    }

    [Fact]
    public async Task Succeed_and_write_the_audit_line_when_redis_is_down()
    {
        Account account = await CreateAccountAsync("REDISDOWN");
        await CreateConfirmedMfaAsync(account.Id);
        _redis.HashGetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns<Task<RedisValue>>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        _cache.RemoveAsync(Arg.Any<string>())
            .Returns<Task<bool>>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        _cache.PublishAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var logs = new CapturingLoggerFactory();

        Assert.True(await MakeService(logs).RemoveMfaAsync(account.Id, _admin));

        CapturedLog audit = Assert.Single(logs.Entries, e => e.Level == LogLevel.Information
            && e.Message.Contains($"Admin {_admin.Value} removed MFA from account {account.Id.Value}"));
        int auditIndex = logs.Entries.IndexOf(audit);
        Assert.Contains(logs.Entries.Skip(auditIndex + 1), e => e.Level == LogLevel.Warning);

        await using AuthDbContext context = _database.CreateDbContext();
        Assert.False(await context.MfaSetups.AnyAsync(m => m.AccountId == account.Id));
    }

    [Fact]
    public async Task Still_publish_the_disconnect_when_the_mfa_state_cleanup_fails()
    {
        Account account = await CreateAccountAsync("HALFDOWN");
        await CreateConfirmedMfaAsync(account.Id);
        _redis.HashGetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns<Task<RedisValue>>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        Assert.True(await MakeService().RemoveMfaAsync(account.Id, _admin));

        await _cache.Received(1).PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, account.Id.Value.ToString());
    }

    private AccountService MakeService(ILoggerFactory? loggerFactory = null) => new(
        loggerFactory ?? NullLoggerFactory.Instance,
        new AccountRepository(_database),
        _jwt,
        Substitute.For<IMFAHashService>(),
        new MfaSetupRepository(_database),
        new DeviceRepository(_database),
        _cache,
        Substitute.For<ISecureRandom>(),
        new DbTransactionRunner<AuthDbContext>(_database),
        new AuthenticationConfig(),
        TestLogin.Password(new AccountRepository(_database), _cache),
        TestLogin.Reauthentication(new AccountRepository(_database), _cache));

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

    private sealed record CapturedLog(LogLevel Level, string Message);

    private sealed class CapturingLoggerFactory : ILoggerFactory, ILogger
    {
        public List<CapturedLog> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new CapturedLog(logLevel, formatter(state, exception)));
    }
}
