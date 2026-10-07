using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Infrastructure.Presence;
using Avalon.Network.Packets.State;
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
/// #639: presence is captured on the tick, in the serial phase after the world update, about once a second by the
/// container's clock, and handed to the Redis writer through one reference the writer takes. Before, the writer's own
/// timer walked the instances' rosters from the thread pool while the tick changed them.
/// </summary>
public class PresenceCaptureShould
{
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly IInstanceRegistry _registry = Substitute.For<IInstanceRegistry>();

    /// <summary>The registry holds these. Built by the caller first: NSubstitute's last-call slot would otherwise be
    /// clobbered by the calls building them inside Returns(...).</summary>
    private void Hold(params IMapInstance[] instances) => _registry.ActiveInstances.Returns(instances);

    private PresenceCapture CreateSut(ushort worldId = 3) =>
        new(Options.Create(new GameConfiguration { WorldId = worldId.ToString() }),
            NullLogger<PresenceCapture>.Instance, _clock);

    private static ICharacter Character(uint id, string name, Vector3 position)
    {
        ICharacter c = Substitute.For<ICharacter>();
        c.Guid.Returns(new ObjectGuid(ObjectType.Character, id));
        c.Name.Returns(name);
        c.Class.Returns(CharacterClass.Wizard);
        c.Position.Returns(position);
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
        IMapInstance i = Substitute.For<IMapInstance>();
        i.InstanceId.Returns(id);
        i.TemplateId.Returns(new MapTemplateId(12));
        i.MapType.Returns(MapType.Normal);
        i.Seed.Returns(-1044266558);
        i.ConfigVersion.Returns("a91f3c7e");
        i.OwnerCharacterId.Returns((uint?)4417);
        // Built before Returns(...): NSubstitute's last-call slot would otherwise be clobbered by c.Guid.
        Dictionary<ObjectGuid, ICharacter> byGuid = characters.ToDictionary(c => c.Guid);
        i.Characters.Returns(byGuid);
        return i;
    }

    [Fact]
    public void Capture_on_the_first_tick_with_every_field_the_admin_view_shows()
    {
        var id = Guid.NewGuid();
        IMapInstance instance = Instance(id, Character(4417, "Nym", new Vector3(412.5f, 1f, -87.25f)));
        _registry.ActiveInstances.Returns([instance]);
        PresenceCapture sut = CreateSut(worldId: 3);

        sut.CaptureIfDue(_registry);
        WorldPresenceSnapshot? snapshot = sut.Take();

        Assert.NotNull(snapshot);
        Assert.Equal((ushort)3, snapshot.WorldId);
        Assert.Equal(_clock.Now.UtcDateTime, snapshot.CapturedAt);
        InstancePresenceSnapshot captured = Assert.Single(snapshot.Instances);
        Assert.Equal((id, (ushort)12, -1044266558, "Normal", "a91f3c7e", (uint?)4417),
            (captured.InstanceId, captured.TemplateId, captured.Seed, captured.MapType, captured.ConfigVersion,
                captured.OwnerCharacterId));
        CharacterPresenceSnapshot nym = Assert.Single(captured.Characters);
        Assert.Equal(new CharacterPresenceSnapshot(4417, "Nym", "Wizard", 412.5f, 1f, -87.25f, 1.57f, 34, 812, 1200,
            "Idle", InCombat: true, Dead: false, LastSeen: _clock.Now.UtcDateTime), nym);
    }

    [Fact]
    public void Capture_about_once_a_second_by_the_containers_clock()
    {
        IMapInstance instance = Instance(Guid.NewGuid(), Character(1, "Nym", Vector3.zero));
        _registry.ActiveInstances.Returns([instance]);
        PresenceCapture sut = CreateSut();

        sut.CaptureIfDue(_registry);
        Assert.NotNull(sut.Take());

        _clock.Now += TimeSpan.FromMilliseconds(999);
        sut.CaptureIfDue(_registry);
        Assert.Null(sut.Take());

        _clock.Now += TimeSpan.FromMilliseconds(1);
        sut.CaptureIfDue(_registry);
        Assert.NotNull(sut.Take());
    }

    /// <summary>A late tick captures once, and the next capture is a full interval after it, not owed straight away.</summary>
    [Fact]
    public void Capture_once_after_a_late_tick_and_count_the_interval_from_it()
    {
        Hold(Instance(Guid.NewGuid(), Character(1, "Nym", Vector3.zero)));
        PresenceCapture sut = CreateSut();
        sut.CaptureIfDue(_registry);
        sut.Take();

        _clock.Now += TimeSpan.FromSeconds(5);
        sut.CaptureIfDue(_registry);
        Assert.NotNull(sut.Take());
        sut.CaptureIfDue(_registry);
        Assert.Null(sut.Take());
    }

    /// <summary>A newer capture replaces one the writer has not taken yet: only the latest is written.</summary>
    [Fact]
    public void Keep_only_the_latest_snapshot_for_the_writer()
    {
        Hold(Instance(Guid.NewGuid(), Character(1, "Nym", Vector3.zero)));
        PresenceCapture sut = CreateSut();
        sut.CaptureIfDue(_registry);
        _clock.Now += TimeSpan.FromSeconds(1);
        sut.CaptureIfDue(_registry);

        Assert.Equal(_clock.Now.UtcDateTime, sut.Take()!.CapturedAt);
    }

    /// <summary>
    /// The last player leaves before the writer took the snapshot that still shows them: the next capture drops it,
    /// so it is never written as live.
    /// </summary>
    [Fact]
    public void Drop_an_untaken_snapshot_once_nobody_is_in_the_world()
    {
        IMapInstance instance = Instance(Guid.NewGuid(), Character(1, "Nym", Vector3.zero));
        IMapInstance emptied = Instance(Guid.NewGuid());
        Hold(instance);
        PresenceCapture sut = CreateSut();
        sut.CaptureIfDue(_registry);

        Hold(emptied);
        _clock.Now += PresenceCapture.Interval;
        sut.CaptureIfDue(_registry);

        Assert.Null(sut.Take());
    }

    /// <summary>One instance whose roster cannot be read costs only itself; the others are captured.</summary>
    [Fact]
    public void Capture_the_other_instances_when_one_throws()
    {
        IMapInstance broken = Substitute.For<IMapInstance>();
        broken.InstanceId.Returns(Guid.NewGuid());
        broken.Characters.Returns(_ => throw new InvalidOperationException("broken roster"));
        IMapInstance healthy = Instance(Guid.NewGuid(), Character(1, "Nym", Vector3.zero));
        _registry.ActiveInstances.Returns([broken, healthy]);
        PresenceCapture sut = CreateSut();

        sut.CaptureIfDue(_registry);

        Assert.Equal(healthy.InstanceId, Assert.Single(sut.Take()!.Instances).InstanceId);
    }

    /// <summary>The capture reads the tick's own state, so it runs on the tick; only the hand-off crosses threads.</summary>
    [Fact]
    public void Hand_the_snapshot_to_a_writer_on_another_thread()
    {
        Hold(Instance(Guid.NewGuid(), Character(1, "Nym", Vector3.zero)));
        PresenceCapture sut = CreateSut();
        sut.CaptureIfDue(_registry);

        WorldPresenceSnapshot? taken = null;
        var writer = new Thread(() => taken = sut.Take());
        writer.Start();
        writer.Join();

        Assert.NotNull(taken);
    }
}
