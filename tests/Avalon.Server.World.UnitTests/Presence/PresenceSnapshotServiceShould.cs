using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Presence;
using Avalon.Network.Packets.State;
using Avalon.Server.World.Presence;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Configuration;
using Avalon.World.Presence;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Presence;

/// <summary>
/// The Redis side of presence: it writes what the tick captured (#639, <see cref="PresenceCapture" />), each snapshot
/// once, under the keys the API reads, and never lets a Redis failure reach the world server.
/// </summary>
public class PresenceSnapshotServiceShould
{
    private readonly IInstanceRegistry _registry = Substitute.For<IInstanceRegistry>();

    /// <summary>The registry holds these. Built by the caller first: NSubstitute's last-call slot would otherwise be
    /// clobbered by the calls building them inside Returns(...).</summary>
    private void Hold(params IMapInstance[] instances) => _registry.ActiveInstances.Returns(instances);
    private readonly IReplicatedCache _cache = Substitute.For<IReplicatedCache>();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    private PresenceCapture Capture(ushort worldId = 1) =>
        new(Options.Create(new GameConfiguration { WorldId = worldId.ToString() }),
            NullLogger<PresenceCapture>.Instance, _clock);

    private PresenceSnapshotService CreateSut(PresenceCapture capture) =>
        new(capture, _cache, NullLogger<PresenceSnapshotService>.Instance);

    /// <summary>A capture that has taken one snapshot of what the registry holds now.</summary>
    private PresenceCapture Captured(ushort worldId = 1)
    {
        PresenceCapture capture = Capture(worldId);
        capture.CaptureIfDue(_registry);
        return capture;
    }

    private static ICharacter Character(uint id, string name)
    {
        ICharacter c = Substitute.For<ICharacter>();
        c.Guid.Returns(new ObjectGuid(ObjectType.Character, id));
        c.Name.Returns(name);
        c.Class.Returns(CharacterClass.Wizard);
        c.Position.Returns(Vector3.zero);
        c.MoveState.Returns(MoveState.Idle);
        return c;
    }

    private static IMapInstance Instance(Guid id, params ICharacter[] characters)
    {
        IMapInstance i = Substitute.For<IMapInstance>();
        i.InstanceId.Returns(id);
        i.TemplateId.Returns(new MapTemplateId(12));
        i.MapType.Returns(MapType.Normal);
        i.Seed.Returns(-1044266558);
        i.ConfigVersion.Returns("a91f3c7e");
        // Built before Returns(...): NSubstitute's last-call slot would otherwise be clobbered by c.Guid.
        Dictionary<ObjectGuid, ICharacter> charactersByGuid = characters.ToDictionary(c => c.Guid);
        i.Characters.Returns(charactersByGuid);
        return i;
    }

    [Fact]
    public async Task Write_nothing_while_the_tick_has_captured_nothing()
    {
        await CreateSut(Capture()).WriteLatestAsync(CancellationToken.None);

        await _cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task Write_nothing_when_instances_hold_no_players()
    {
        Hold(Instance(Guid.NewGuid()));

        await CreateSut(Captured()).WriteLatestAsync(CancellationToken.None);

        await _cache.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task Write_the_world_snapshot_with_the_presence_ttl()
    {
        Hold(Instance(Guid.NewGuid(), Character(4417, "Nym")));

        await CreateSut(Captured(worldId: 3)).WriteLatestAsync(CancellationToken.None);

        await _cache.Received(1).SetAsync(
            "world:3:presence",
            Arg.Is<string>(json => json.Contains("\"name\":\"Nym\"")),
            CacheKeys.PresenceTtl);
    }

    [Fact]
    public async Task Write_a_character_index_entry_per_player()
    {
        Hold(Instance(Guid.NewGuid(), Character(4417, "Nym"), Character(9002, "Kel")));

        await CreateSut(Captured(worldId: 3)).WriteLatestAsync(CancellationToken.None);

        await _cache.Received(1).SetAsync("presence:world:3:character:4417", Arg.Any<string>(), CacheKeys.PresenceTtl);
        await _cache.Received(1).SetAsync("presence:world:3:character:9002", Arg.Any<string>(), CacheKeys.PresenceTtl);
    }

    /// <summary>
    /// A snapshot is written once. A tick that stalls captures nothing new, so nothing more is written and the keys
    /// expire rather than show a stale position as live.
    /// </summary>
    [Fact]
    public async Task Write_each_capture_once()
    {
        Hold(Instance(Guid.NewGuid(), Character(4417, "Nym")));
        PresenceSnapshotService sut = CreateSut(Captured(worldId: 3));

        await sut.WriteLatestAsync(CancellationToken.None);
        await sut.WriteLatestAsync(CancellationToken.None);

        await _cache.Received(1).SetAsync("world:3:presence", Arg.Any<string>(), CacheKeys.PresenceTtl);
    }

    /// <summary>
    /// Character ids are unique only per world (#556), and every world server writes to one Redis:
    /// two worlds' character 7 must land under two keys, each naming its own world and instance.
    /// </summary>
    [Fact]
    public async Task Keep_both_worlds_entries_for_the_same_character_id()
    {
        Dictionary<string, string> written = [];
        _cache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>())
            .Returns(call =>
            {
                written[call.ArgAt<string>(0)] = call.ArgAt<string>(1);
                return Task.FromResult(true);
            });
        var instanceOne = Guid.NewGuid();
        var instanceTwo = Guid.NewGuid();

        // Built before Returns(...): see the note in Instance about NSubstitute's last-call slot.
        IMapInstance worldOne = Instance(instanceOne, Character(7, "Nym"));
        IMapInstance worldTwo = Instance(instanceTwo, Character(7, "Zed"));

        _registry.ActiveInstances.Returns([worldOne]);
        await CreateSut(Captured(worldId: 1)).WriteLatestAsync(CancellationToken.None);
        _registry.ActiveInstances.Returns([worldTwo]);
        await CreateSut(Captured(worldId: 2)).WriteLatestAsync(CancellationToken.None);

        CharacterPresenceIndex? one = PresenceJson.Deserialize<CharacterPresenceIndex>(written[CacheKeys.CharacterPresenceIndex(1, 7)]);
        CharacterPresenceIndex? two = PresenceJson.Deserialize<CharacterPresenceIndex>(written[CacheKeys.CharacterPresenceIndex(2, 7)]);
        Assert.Equal(((ushort)1, instanceOne), (one!.WorldId, one.InstanceId));
        Assert.Equal(((ushort)2, instanceTwo), (two!.WorldId, two.InstanceId));
    }

    [Fact]
    public async Task Carry_seed_and_config_version_into_the_snapshot()
    {
        Hold(Instance(Guid.NewGuid(), Character(4417, "Nym")));
        string? captured = null;
        await _cache.SetAsync(
            Arg.Is<string>(k => k == "world:1:presence"),
            Arg.Do<string>(v => captured = v),
            Arg.Any<TimeSpan?>());

        await CreateSut(Captured()).WriteLatestAsync(CancellationToken.None);

        WorldPresenceSnapshot? snap = PresenceJson.Deserialize<WorldPresenceSnapshot>(captured!);
        Assert.Equal(-1044266558, snap!.Instances[0].Seed);
        Assert.Equal("a91f3c7e", snap.Instances[0].ConfigVersion);
        Assert.Equal("Normal", snap.Instances[0].MapType);
    }

    [Fact]
    public async Task Not_throw_when_the_cache_write_fails()
    {
        Hold(Instance(Guid.NewGuid(), Character(4417, "Nym")));
        _cache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>())
              .Returns<Task<bool>>(_ => throw new InvalidOperationException("redis down"));

        await CreateSut(Captured()).WriteLatestAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Not_throw_when_the_cache_throws_a_cancellation_unrelated_to_our_token()
    {
        // TaskCanceledException derives from OperationCanceledException. A bare
        // `catch (OperationCanceledException) { throw; }` would let this one escape and
        // stop the world server via BackgroundService's default StopHost behavior, even
        // though the CancellationToken this write was given was never cancelled.
        Hold(Instance(Guid.NewGuid(), Character(4417, "Nym")));
        _cache.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>())
              .Returns<Task<bool>>(_ => throw new TaskCanceledException("redis reconnect"));

        await CreateSut(Captured()).WriteLatestAsync(CancellationToken.None);
    }

    /// <summary>The writer never walks the instances: only the tick's capture reads simulation state.</summary>
    [Fact]
    public async Task Read_no_simulation_state_when_it_writes()
    {
        Hold(Instance(Guid.NewGuid(), Character(4417, "Nym")));
        PresenceCapture capture = Captured();
        _registry.ClearReceivedCalls();

        await CreateSut(capture).WriteLatestAsync(CancellationToken.None);

        _ = _registry.DidNotReceive().ActiveInstances;
        await _cache.Received().SetAsync("world:1:presence", Arg.Any<string>(), CacheKeys.PresenceTtl);
    }
}
