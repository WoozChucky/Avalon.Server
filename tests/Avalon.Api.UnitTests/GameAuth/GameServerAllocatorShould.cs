using Avalon.Api.Services;
using Avalon.Api.Worlds;
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

namespace Avalon.Api.UnitTests.GameAuth;

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
    private readonly Avalon.Domain.Auth.World _world = new() { Id = new WorldId(1), Name = "World", Host = "world.example.test", Port = 21000, MinVersion = "0.0.1", Version = "0.0.1", MaintenanceRevision = 1 };
    private readonly GameContextRecord _context = new() { Id = Guid.NewGuid(), AccountId = 7, ProtocolVersion = "1", Environment = "production", State = "authorized", CredentialDigest = "digest", RefreshDigest = "digest" };
    private readonly GameServerAllocator _allocator;
    public GameServerAllocatorShould()
    {
        _accounts.FindByIdAsync(_account.Id, false, Arg.Any<CancellationToken>()).Returns(_account);
        _worlds.FindByIdAsync(_world.Id, false, Arg.Any<CancellationToken>()).Returns(_world);
        _databases.IsAvailable(_world.Id).Returns(true);
        _readiness.IsReadyAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _repositories.Characters(_world.Id).Returns(_characters);
        _allocator = new(_worlds, _accounts, _databases, _readiness, _repositories,
            Options.Create(new GameWorkloadConfiguration { Servers = [new GameServerDefinition { ServerId = "world-1", WorldId = 1, TlsServerName = "world.example.test", TlsCertificateSha256 = new string('A', 64), ClientCertificateSha256 = new string('B', 64) }] }), _clock);
    }
    private Task<GameWorldDestination?> Find(GameContextRecord? context = null, uint? character = null) => _allocator.FindAsync(context ?? _context, 1, character, CancellationToken.None);

    [Fact]
    public async Task Bind_destinations_to_the_configured_workload_and_tls_identity()
    {
        var destination = await Find();
        Assert.NotNull(destination);
        Assert.Equal("world-1", destination!.ServerId);
        Assert.Equal("world.example.test", destination.TlsServerName);
        Assert.Equal(new string('A', 64), destination.TlsCertificateSha256);
        Assert.Equal("0.0.1", destination.MinVersion);
        Assert.Single(await _allocator.ListAsync(_context, CancellationToken.None));
        Assert.Null(await _allocator.FindAsync(_context, 2, null, CancellationToken.None));
    }
    [Fact]
    public async Task Gate_current_account_world_access_readiness_protocol_and_database_availability()
    {
        Assert.Null(await Find(_context with { ProtocolVersion = "legacy" }));
        Assert.Null(await Find(_context with { SessionEpoch = 1 }));
        _account.Locked = true;
        Assert.Null(await Find());
        _account.Locked = false;
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
