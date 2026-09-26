using System.Text;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Handlers;
using Avalon.Infrastructure.Login;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using AvalonWorld = Avalon.Domain.Auth.World;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// #484: the auth server cleared <c>Online</c> (and stored a world key) by writing back the whole
/// account row it had read, so a ban or a lock written in between was put back to the old values.
/// Each write below has a ban and a lock land right after its read, over the real repository and a
/// real relational schema, and both must survive it.
/// </summary>
public sealed class OfflineWriteRaceShould : IDisposable
{
    private readonly AuthSqlite _database = new();
    private readonly AccountRepository _accounts;

    public OfflineWriteRaceShould()
    {
        _accounts = new AccountRepository(_database);
    }

    public void Dispose() => _database.Dispose();

    /// <summary>The auth-server connection whose login set <c>Online</c>.</summary>
    private readonly Guid _session = Guid.NewGuid();

    private async Task<Account> OnlineAccountAsync()
    {
        Account account = await _accounts.CreateAsync(new Account
        {
            Username = "RACEUSER",
            Email = "race@example.com",
            Salt = new byte[16],
            Verifier = Encoding.UTF8.GetBytes(BCrypt.Net.BCrypt.HashPassword(TestPasswords.Valid)),
            JoinDate = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow.AddMinutes(-10),
            TotalTime = 100,
        });
        await using AuthDbContext context = _database.CreateDbContext();
        await context.Accounts.Where(a => a.Id == account.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Online, true)
                .SetProperty(a => a.OnlineSessionId, (Guid?)_session));
        return account;
    }

    private async Task<Account> StoredAsync(AccountId id)
    {
        await using AuthDbContext context = _database.CreateDbContext();
        return await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private DateTime _lockedUntil;

    /// <summary>What an admin ban and a lock by failed logins write, landing after the handler's read.</summary>
    private async Task BanAndLockAsync(AccountId id)
    {
        _lockedUntil = DateTime.UtcNow.AddMinutes(15);
        await using AuthDbContext context = _database.CreateDbContext();
        await context.Accounts.Where(a => a.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.Status, AccountStatus.Banned)
            .SetProperty(a => a.Locked, true)
            .SetProperty(a => a.FailedLogins, 5)
            .SetProperty(a => a.LockedUntil, (DateTime?)_lockedUntil));
    }

    private async Task AssertBanAndLockSurvivedAsync(AccountId id)
    {
        Account stored = await StoredAsync(id);
        Assert.Equal(AccountStatus.Banned, stored.Status);
        Assert.True(stored.Locked);
        Assert.Equal(5, stored.FailedLogins);
        Assert.NotNull(stored.LockedUntil);
        Assert.Equal(_lockedUntil, stored.LockedUntil!.Value, TimeSpan.FromMilliseconds(1));
    }

    private static IOptions<AuthConfiguration> Options() =>
        Microsoft.Extensions.Options.Options.Create(new AuthConfiguration());

    /// <summary>A connection on a server holding no connections, so the account's session is not found.</summary>
    private static IAuthConnection ConnectionWithNoSessions()
    {
        IAuthConnection connection = Substitute.For<IAuthConnection>();
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.RemoteEndPoint.Returns("127.0.0.1:12345");
        var hosting = Substitute.For<IOptions<HostingConfiguration>>();
        hosting.Value.Returns(new HostingConfiguration { Port = 0, Host = "127.0.0.1" });
        var security = Substitute.For<IOptions<HostingSecurity>>();
        security.Value.Returns(new HostingSecurity());
        var server = new AuthServer(Substitute.For<IServiceProvider>(), Substitute.For<IPacketManager>(),
            NullLoggerFactory.Instance, Substitute.For<IAccountRepository>(), Substitute.For<IReplicatedCache>(),
            hosting, security);
        connection.Server.Returns(server);
        return connection;
    }

    [Fact]
    public async Task Keep_a_ban_and_a_lock_written_while_a_login_cleared_a_stale_online_flag()
    {
        Account account = await OnlineAccountAsync();
        var stale = new StaleAccountRepository(_accounts) { AfterRead = () => BanAndLockAsync(account.Id) };
        var handler = new CAuthHandler(NullLoggerFactory.Instance, stale, Substitute.For<IReplicatedCache>(),
            Substitute.For<IMFAHashService>(), Substitute.For<IMfaSetupRepository>(), Options(),
            new BCryptPasswordVerifier());

        await handler.ExecuteAsync(new AuthPacketContext<CAuthPacket>
        {
            Packet = new CAuthPacket { Username = "raceuser", Password = TestPasswords.Valid },
            Connection = ConnectionWithNoSessions(),
        });

        await AssertBanAndLockSurvivedAsync(account.Id);
        Assert.False((await StoredAsync(account.Id)).Online);
    }

    [Fact]
    public async Task Keep_a_ban_and_a_lock_written_while_an_mfa_verify_cleared_a_stale_online_flag()
    {
        Account account = await OnlineAccountAsync();
        var stale = new StaleAccountRepository(_accounts) { AfterRead = () => BanAndLockAsync(account.Id) };
        IMFAService mfa = Substitute.For<IMFAService>();
        mfa.VerifyMFAAsync("hash", "123456", Arg.Any<CancellationToken>())
            .Returns(new MFAVerifyResult(true, account.Id));
        IMFAHashService hashes = Substitute.For<IMFAHashService>();
        hashes.GetAccountIdAsync(Arg.Any<string>()).Returns(account.Id);
        hashes.RecordAttemptAsync(account.Id).Returns(1L);
        var handler = new CMFAVerifyHandler(NullLoggerFactory.Instance, mfa, stale, Substitute.For<IReplicatedCache>(),
            hashes, Options());

        await handler.ExecuteAsync(new AuthPacketContext<CMFAVerifyPacket>
        {
            Packet = new CMFAVerifyPacket { MfaHash = "hash", Code = "123456" },
            Connection = ConnectionWithNoSessions(),
        });

        await AssertBanAndLockSurvivedAsync(account.Id);
        Assert.False((await StoredAsync(account.Id)).Online);
    }

    [Fact]
    public async Task Keep_a_ban_and_a_lock_written_while_a_disconnect_was_being_saved()
    {
        Account account = await OnlineAccountAsync();
        var stale = new StaleAccountRepository(_accounts) { AfterRead = () => BanAndLockAsync(account.Id) };

        await AuthConnection.RecordDisconnectAsync(stale, account.Id, _session, account.LastLogin.AddSeconds(600));

        await AssertBanAndLockSurvivedAsync(account.Id);
        Account stored = await StoredAsync(account.Id);
        Assert.False(stored.Online);
        Assert.Equal(700, stored.TotalTime);
    }

    /// <summary>The start-up reset read every row and wrote each back whole; it is now one statement.</summary>
    [Fact]
    public async Task Clear_every_online_flag_at_start_up_and_nothing_else()
    {
        Account account = await OnlineAccountAsync();
        await BanAndLockAsync(account.Id);

        await _accounts.MarkAllOfflineAsync();

        await AssertBanAndLockSurvivedAsync(account.Id);
        Account stored = await StoredAsync(account.Id);
        Assert.False(stored.Online);
        Assert.Equal(100, stored.TotalTime);
    }

    [Fact]
    public async Task Keep_a_ban_and_a_lock_written_while_a_world_key_was_being_stored()
    {
        Account account = await OnlineAccountAsync();
        var stale = new StaleAccountRepository(_accounts) { AfterRead = () => BanAndLockAsync(account.Id) };
        IWorldRepository worlds = Substitute.For<IWorldRepository>();
        worlds.FindByIdAsync(Arg.Any<WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new AvalonWorld
        {
            Id = new WorldId(1),
            Name = "Test World",
            Host = "localhost",
            Port = 7001,
            MinVersion = "0.0.1",
            Version = "0.0.1",
            AccessLevelRequired = AccountAccessLevel.Player,
        });
        IReplicatedCache cache = Substitute.For<IReplicatedCache>();
        cache.SetNxAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns(true);
        ISecureRandom random = Substitute.For<ISecureRandom>();
        random.GetBytes(32).Returns(Enumerable.Repeat((byte)7, 32).ToArray());
        IAuthConnection connection = Substitute.For<IAuthConnection>();
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.AccountId.Returns(account.Id);
        var handler = new CWorldSelectHandler(NullLoggerFactory.Instance, cache, stale, worlds, random);

        await handler.ExecuteAsync(new AuthPacketContext<CWorldSelectPacket>
        {
            Packet = new CWorldSelectPacket { WorldId = new WorldId(1) },
            Connection = connection,
        });

        await AssertBanAndLockSurvivedAsync(account.Id);
        Assert.Equal(Enumerable.Repeat((byte)7, 32).ToArray(), (await StoredAsync(account.Id)).SessionKey);
    }
}
