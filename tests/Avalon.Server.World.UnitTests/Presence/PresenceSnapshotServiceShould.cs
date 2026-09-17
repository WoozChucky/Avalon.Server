using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Presence;
using Avalon.Network.Packets.State;
using Avalon.Server.World.Presence;
using Avalon.World.Configuration;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Presence;

public class PresenceSnapshotServiceShould
{
    private readonly IInstanceRegistry _registry = Substitute.For<IInstanceRegistry>();
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();

    private PresenceSnapshotService CreateSut(ushort worldId = 1)
    {
        var config = new GameConfiguration { WorldId = worldId.ToString() };
        return new PresenceSnapshotService(
            _registry, _cache, Options.Create(config), NullLogger<PresenceSnapshotService>.Instance);
    }

    private static ICharacter Character(uint id, string name, Vector3 pos)
    {
        var c = Substitute.For<ICharacter>();
        c.Guid.Returns(new ObjectGuid(ObjectType.Character, id));
        c.Name.Returns(name);
        c.Class.Returns(CharacterClass.Wizard);
        c.Position.Returns(pos);
        c.Orientation.Returns(new Vector3(0, 1.57f, 0));
        c.Level.Returns((ushort)34);
        c.CurrentHealth.Returns(812u);
        c.Health.Returns(1200u);
        c.MoveState.Returns(MoveState.Idle);
        c.IsInCombat.Returns(true);
        c.IsDead.Returns(false);
        return c;
    }

    private static IMapInstance Instance(Guid id, params ICharacter[] characters)
    {
        var i = Substitute.For<IMapInstance>();
        i.InstanceId.Returns(id);
        i.TemplateId.Returns(new MapTemplateId(12));
        i.MapType.Returns(MapType.Normal);
        i.Seed.Returns(-1044266558);
        i.ConfigVersion.Returns("a91f3c7e");
        i.OwnerCharacterId.Returns((uint?)4417);
        // NSubstitute tracks "the last call made" on a single thread-local slot, shared
        // across every substitute. Building this dictionary inline as the Returns(...)
        // argument would call c.Guid on each character substitute while i.Characters'
        // pending call is still awaiting configuration, clobbering it. Materializing it
        // into a local first keeps those calls out of the Returns(...) expression.
        Dictionary<ObjectGuid, ICharacter> charactersByGuid = characters.ToDictionary(c => c.Guid);
        i.Characters.Returns(charactersByGuid);
        return i;
    }

    /// <summary>
    /// An instance whose roster throws when read, simulating the 60 Hz simulation tick
    /// mutating <c>MapInstance.Characters</c> (a plain, non-concurrent dictionary) while
    /// PresenceSnapshotService's timer walks it from a thread-pool thread.
    /// </summary>
    private static IMapInstance InstanceWithRacingRoster(Guid id)
    {
        var i = Substitute.For<IMapInstance>();
        i.InstanceId.Returns(id);
        i.TemplateId.Returns(new MapTemplateId(12));
        i.MapType.Returns(MapType.Normal);
        i.Seed.Returns(0);
        i.ConfigVersion.Returns(string.Empty);
        i.OwnerCharacterId.Returns((uint?)null);
        i.Characters.Returns(_ => throw new InvalidOperationException(
            "Collection was modified; enumeration operation may not execute."));
        return i;
    }

    [Fact]
    public async Task Should_write_nothing_when_no_instances_are_active()
    {
        _registry.ActiveInstances.Returns([]);

        await CreateSut().CaptureOnceAsync(CancellationToken.None);

        await _cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task Should_write_nothing_when_instances_hold_no_players()
    {
        IMapInstance instance = Instance(Guid.NewGuid());
        _registry.ActiveInstances.Returns([instance]);

        await CreateSut().CaptureOnceAsync(CancellationToken.None);

        await _cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task Should_write_the_world_snapshot_with_the_presence_ttl()
    {
        ICharacter nym = Character(4417, "Nym", new Vector3(412.5f, 0f, -87.25f));
        IMapInstance instance = Instance(Guid.NewGuid(), nym);
        _registry.ActiveInstances.Returns([instance]);

        await CreateSut(worldId: 3).CaptureOnceAsync(CancellationToken.None);

        await _cache.Received(1).SetAsync(
            "world:3:presence",
            Arg.Is<string>(json => json.Contains("\"name\":\"Nym\"")),
            CacheKeys.PresenceTtl);
    }

    [Fact]
    public async Task Should_write_a_character_index_entry_per_player()
    {
        ICharacter nym = Character(4417, "Nym", Vector3.zero);
        ICharacter kel = Character(9002, "Kel", Vector3.zero);
        IMapInstance instance = Instance(Guid.NewGuid(), nym, kel);
        _registry.ActiveInstances.Returns([instance]);

        await CreateSut(worldId: 3).CaptureOnceAsync(CancellationToken.None);

        await _cache.Received(1).SetAsync(
            "presence:character:4417", Arg.Any<string>(), CacheKeys.PresenceTtl);
        await _cache.Received(1).SetAsync(
            "presence:character:9002", Arg.Any<string>(), CacheKeys.PresenceTtl);
    }

    [Fact]
    public async Task Should_carry_seed_and_config_version_into_the_snapshot()
    {
        ICharacter nym = Character(4417, "Nym", Vector3.zero);
        IMapInstance instance = Instance(Guid.NewGuid(), nym);
        _registry.ActiveInstances.Returns([instance]);
        string? captured = null;
        await _cache.SetAsync(
            Arg.Is<string>(k => k == "world:1:presence"),
            Arg.Do<string>(v => captured = v),
            Arg.Any<TimeSpan?>());

        await CreateSut().CaptureOnceAsync(CancellationToken.None);

        WorldPresenceSnapshot? snap = PresenceJson.Deserialize<WorldPresenceSnapshot>(captured!);
        Assert.Equal(-1044266558, snap!.Instances[0].Seed);
        Assert.Equal("a91f3c7e", snap.Instances[0].ConfigVersion);
        Assert.Equal("Normal", snap.Instances[0].MapType);
    }

    [Fact]
    public async Task Should_not_throw_when_the_cache_write_fails()
    {
        ICharacter nym = Character(4417, "Nym", Vector3.zero);
        IMapInstance instance = Instance(Guid.NewGuid(), nym);
        _registry.ActiveInstances.Returns([instance]);
        _cache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>())
              .Returns<Task<bool>>(_ => throw new InvalidOperationException("redis down"));

        await CreateSut().CaptureOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Should_not_throw_when_the_cache_throws_a_cancellation_unrelated_to_our_token()
    {
        // TaskCanceledException derives from OperationCanceledException. A bare
        // `catch (OperationCanceledException) { throw; }` would let this one escape and
        // stop the world server via BackgroundService's default StopHost behavior, even
        // though the CancellationToken this capture was given was never cancelled.
        ICharacter nym = Character(4417, "Nym", Vector3.zero);
        IMapInstance instance = Instance(Guid.NewGuid(), nym);
        _registry.ActiveInstances.Returns([instance]);
        _cache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>())
              .Returns<Task<bool>>(_ => throw new TaskCanceledException("redis reconnect"));

        await CreateSut().CaptureOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Should_write_other_instances_when_one_instance_throws_while_being_walked()
    {
        // The racing instance is listed first so this also proves the loop continues
        // past it rather than aborting the whole capture.
        IMapInstance racing = InstanceWithRacingRoster(Guid.NewGuid());
        ICharacter nym = Character(4417, "Nym", Vector3.zero);
        IMapInstance healthy = Instance(Guid.NewGuid(), nym);
        _registry.ActiveInstances.Returns([racing, healthy]);

        await CreateSut(worldId: 3).CaptureOnceAsync(CancellationToken.None);

        await _cache.Received(1).SetAsync(
            "world:3:presence",
            Arg.Is<string>(json => json.Contains("\"name\":\"Nym\"")),
            CacheKeys.PresenceTtl);
    }

    [Fact]
    public async Task Should_write_nothing_and_log_no_warning_when_the_registry_is_not_ready_yet()
    {
        // Production's accessor (IWorld.InstanceRegistry) is null for several seconds at
        // every boot, until World.LoadAsync finishes. That must read as "nothing to
        // publish yet", not an error worth a warning-level log.
        var logger = new CapturingLogger();
        var sut = new PresenceSnapshotService(
            () => (IInstanceRegistry?)null, _cache, Options.Create(new GameConfiguration { WorldId = "1" }), logger);

        await sut.CaptureOnceAsync(CancellationToken.None);

        await _cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>());
        Assert.DoesNotContain(LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public async Task Should_defer_reading_the_registry_until_capture_runs()
    {
        // Production wiring resolves IWorld.InstanceRegistry via this accessor, and that
        // property does not exist until World.LoadAsync runs -- well after this hosted
        // service is constructed. The accessor must not be invoked at construction time,
        // only when a capture actually happens.
        var accessorCalls = 0;
        IInstanceRegistry Accessor()
        {
            accessorCalls++;
            return _registry;
        }
        _registry.ActiveInstances.Returns([]);

        var sut = new PresenceSnapshotService(
            Accessor, _cache, Options.Create(new GameConfiguration { WorldId = "1" }),
            NullLogger<PresenceSnapshotService>.Instance);

        Assert.Equal(0, accessorCalls);

        await sut.CaptureOnceAsync(CancellationToken.None);

        Assert.Equal(1, accessorCalls);
    }

    private sealed class CapturingLogger : ILogger<PresenceSnapshotService>
    {
        private readonly List<LogLevel> _levels = [];

        public IReadOnlyList<LogLevel> Levels
        {
            get { lock (_levels) return [.. _levels]; }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_levels) _levels.Add(logLevel);
        }
    }
}
