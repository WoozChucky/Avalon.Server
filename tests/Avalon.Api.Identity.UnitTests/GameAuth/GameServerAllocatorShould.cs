using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Identity.Services;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public sealed class GameServerAllocatorShould
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly IWorldRepository _worlds = Substitute.For<IWorldRepository>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IWorldDatabases _databases = Substitute.For<IWorldDatabases>();
    private readonly IWorldReadiness _readiness = Substitute.For<IWorldReadiness>();
    private readonly IWorldRepositories _repositories = Substitute.For<IWorldRepositories>();
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly Account _account = new() { Id = new AccountId(7), Username = "PLAYER", Email = "p@example.test", Salt = [1], Verifier = [2], JoinDate = DateTime.UnixEpoch };
    private readonly Avalon.Domain.Auth.World _world = new() { Id = new WorldId(1), Name = "World", Host = "world.example.test", Port = 21000, MinVersion = "0.2.0", Version = "0.2.0", MaintenanceRevision = 1 };
    private readonly GameContextRecord _context = new() { Id = Guid.NewGuid(), SteamAppId = 480, ApplicationKey = "steam.main", AccountId = 7, ProtocolVersion = "0.2.0", Environment = "production", State = "authorized", CredentialDigest = "digest", RefreshDigest = "digest" };
    private readonly GameServerAllocator _allocator;

    [Fact]
    public async Task Playtest_lists_only_PTR_and_rejects_direct_other_worlds_even_for_admins()
    {
        var ptr = new Avalon.Domain.Auth.World { Id = new WorldId(3), Name = "PTR", Host = "ptr.example.test", Port = 21000, MinVersion = "0.2.0", Version = "0.2.0", AccessLevelRequired = AccountAccessLevel.PTR };
        _worlds.FindByIdAsync(ptr.Id, false, Arg.Any<CancellationToken>()).Returns(ptr);
        _databases.IsAvailable(ptr.Id).Returns(true);
        _readiness.IsReadyAsync(3, Arg.Any<CancellationToken>()).Returns(true);
        IOptions<GameWorkloadConfiguration> workload = Options.Create(new GameWorkloadConfiguration { Servers = [new GameServerDefinition { ServerId = "world-1", WorldId = 1 }, new GameServerDefinition { ServerId = "world-2", WorldId = 2 }, new GameServerDefinition { ServerId = "world-3", WorldId = 3 }] });
        var config = new StoreAuthenticationConfiguration { SteamAppId = 480, SteamPlaytest = new() { Enabled = true, AppId = 2514590, AllowedWorldIds = [3] } };
        var allocator = new GameServerAllocator(_worlds, _accounts, _databases, _readiness, _repositories, workload, _clock, new GameApplicationAccessPolicy(Options.Create(config)));
        GameContextRecord context = _context with { SteamAppId = 2514590, ApplicationKey = "steam.playtest" };
        _account.AccessLevel |= AccountAccessLevel.Admin;
        Assert.Equal((ushort)3, Assert.Single(await allocator.ListAsync(context, default)).WorldId);
        Assert.Null(await allocator.FindAsync(context, 1, null, default));
        Assert.Null(await allocator.FindAsync(context, 2, null, default));
        Assert.NotNull(await allocator.FindAsync(context with { SteamAppId = 480, ApplicationKey = "steam.main" }, 1, null, default));
        _readiness.IsReadyAsync(3, Arg.Any<CancellationToken>()).Returns(false);
        Assert.Empty(await allocator.ListAsync(context, default));
        _readiness.IsReadyAsync(3, Arg.Any<CancellationToken>()).Returns(true);
        _repositories.Characters(ptr.Id).Returns(_characters);
        Assert.Null(await allocator.FindAsync(context, 3, 42, default));
        ptr.MaintenanceEnabled = true;
        ptr.MaintenanceDeadlineUtc = _clock.GetUtcNow().UtcDateTime;
        _account.AccessLevel &= ~AccountAccessLevel.Admin;
        Assert.Null(await allocator.FindAsync(context, 3, null, default));
        ptr.MaintenanceEnabled = false;
        Assert.NotNull(await allocator.FindAsync(context, 3, null, default));
        Assert.Equal(AccountAccessLevel.Player, _account.AccessLevel);
        Assert.Null(await allocator.FindAsync(context with { SteamAppId = 480, ApplicationKey = "steam.main" }, 3, null, default));
        Assert.Null(await allocator.FindAsync(context with { SteamAppId = 0, ApplicationKey = null }, 3, null, default));
        Assert.Null(await allocator.FindAsync(context with { SteamAppId = 999, ApplicationKey = "unknown" }, 3, null, default));
        foreach (AccountAccessLevel required in new[] { AccountAccessLevel.GameMaster, AccountAccessLevel.Admin, AccountAccessLevel.Console, AccountAccessLevel.Tournament, (AccountAccessLevel)0 })
        {
            ptr.AccessLevelRequired = required;
            Assert.Null(await allocator.FindAsync(context, 3, null, default));
        }
        ptr.AccessLevelRequired = AccountAccessLevel.PTR;
        Assert.NotNull(await allocator.FindAsync(context, 3, null, default));
        config.SteamPlaytest.Enabled = false;
        Assert.Empty(await allocator.ListAsync(context, default));
    }
    public GameServerAllocatorShould()
    {
        _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns(_account);
        _worlds.FindByIdAsync(_world.Id, false, Arg.Any<CancellationToken>()).Returns(_world);
        _databases.IsAvailable(_world.Id).Returns(true);
        _readiness.IsReadyAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _repositories.Characters(_world.Id).Returns(_characters);
        _allocator = new(_worlds, _accounts, _databases, _readiness, _repositories,
            Options.Create(new GameWorkloadConfiguration { Servers = [new GameServerDefinition { ServerId = "world-1", WorldId = 1, TlsServerName = "world.example.test", TlsCertificateSha256 = new string('A', 64), ClientCertificateSha256 = new string('B', 64) }] }), _clock, new GameApplicationAccessPolicy(Options.Create(new StoreAuthenticationConfiguration { SteamAppId = 480 })));
    }
    private Task<GameWorldDestination?> Find(GameContextRecord? context = null, uint? character = null) => _allocator.FindAsync(context ?? _context, 1, character, CancellationToken.None);

    [Fact]
    public async Task Bind_destinations_to_the_configured_workload_and_tls_identity()
    {
        GameWorldDestination? destination = await Find();
        Assert.NotNull(destination);
        Assert.Equal("world-1", destination!.ServerId);
        Assert.Equal("world.example.test", destination.TlsServerName);
        Assert.Equal(new string('A', 64), destination.TlsCertificateSha256);
        Assert.Equal("0.2.0", destination.MinVersion);
        Assert.Single(await _allocator.ListAsync(_context, CancellationToken.None));
        Assert.Null(await _allocator.FindAsync(_context, 2, null, CancellationToken.None));
    }
    [Fact]
    public async Task Gate_current_account_world_access_readiness_protocol_and_database_availability()
    {
        Assert.Null(await Find(_context with { ProtocolVersion = "legacy" }));
        Assert.Null(await Find(_context with { ProtocolVersion = "1" }));
        Assert.Null(await Find(_context with { ProtocolVersion = "0.1.0" }));
        Assert.Null(await Find(_context with { SessionEpoch = 1 }));
        // A consolidating account is frozen; a password lock is not asked (#882).
        _account.GameplayConsolidationId = Guid.NewGuid();
        Assert.Null(await Find());
        _account.GameplayConsolidationId = null;
        _world.AccessLevelRequired = AccountAccessLevel.Admin;
        Assert.Null(await Find());
        _world.AccessLevelRequired = AccountAccessLevel.Player;
        _readiness.IsReadyAsync(1, Arg.Any<CancellationToken>()).Returns(false);
        Assert.Null(await Find());
        _readiness.IsReadyAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _databases.IsAvailable(_world.Id).Returns(false);
        Assert.Null(await Find());
    }
    [Fact]
    public async Task Keep_the_existing_maintenance_cutoff_and_character_ownership_gates()
    {
        _world.MaintenanceEnabled = true;
        _world.MaintenanceDeadlineUtc = _clock.GetUtcNow().UtcDateTime.AddSeconds(1);
        Assert.NotNull(await Find());
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await Find());
        _account.AccessLevel |= AccountAccessLevel.Admin;
        Assert.NotNull(await Find());
        Assert.Null(await Find(character: 42));
        await _characters.Received().FindByIdAndAccountAsync(new CharacterId(42), _account.Id, Arg.Any<CancellationToken>());
    }
}
