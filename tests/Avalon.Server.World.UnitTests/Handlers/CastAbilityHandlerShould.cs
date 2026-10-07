using Avalon.Combat;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// Every refused cast is answered with exactly one SAbilityNotReadyPacket naming its reason (#512);
/// only a connection with no character hears nothing. A cast aims at a direction or a ground point,
/// never at a unit (#164): TargetGuid is ignored, and there is no range or facing check.
/// </summary>
public class CastAbilityHandlerShould
{
    [Fact]
    public void Admit_a_god_mode_cast_despite_gcd_ability_cooldown_and_no_power()
    {
        var f = new GodFixture();
        // 50 ms after the last start, inside the 200 ms global cooldown, so the bypass is really exercised.
        f.Character.LastCastStartTime = f.Now.AddMilliseconds(-50);
        DateTime previousStart = f.Character.LastCastStartTime;
        f.Character.CurrentPower = 0;
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Cost = 30 });
        ability.CooldownTimer.Returns(5f);

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        f.Instance.Received(1).RunInstantAbility(f.Character, Arg.Any<AbilityAim>(), ability);
        Assert.Empty(f.SentPackets());
        Assert.Equal(previousStart, f.Character.LastCastStartTime);
    }

    [Fact]
    public void Preserve_other_cast_requirements_in_god_mode()
    {
        var dead = new GodFixture();
        dead.Character.IsDead = true;
        dead.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });
        dead.Cast(new CCastAbilityPacket { AbilityId = 1 });
        Assert.Equal(CastRejectReason.Dead, dead.SingleRefusal().Reason);

        var casting = new GodFixture();
        IAbility active = casting.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });
        active.Casting = true;
        casting.Cast(new CCastAbilityPacket { AbilityId = 1 });
        Assert.Equal(CastRejectReason.AlreadyCasting, casting.SingleRefusal().Reason);

        var notOwned = new GodFixture();
        notOwned.Cast(new CCastAbilityPacket { AbilityId = 1 });
        Assert.Equal(CastRejectReason.NotOwned, notOwned.SingleRefusal().Reason);

        var missingAim = new GodFixture();
        missingAim.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", AimMode = AbilityAimMode.Cursor });
        missingAim.Cast(new CCastAbilityPacket { AbilityId = 1 });
        Assert.Equal(CastRejectReason.NoAimPoint, missingAim.SingleRefusal().Reason);

        var outOfCombat = new GodFixture();
        outOfCombat.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Flags = AbilityFlags.RequiresInCombat });
        outOfCombat.Cast(new CCastAbilityPacket { AbilityId = 1 });
        Assert.Equal(CastRejectReason.RequiresInCombat, outOfCombat.SingleRefusal().Reason);
    }

    private sealed class GodFixture
    {
        public CharacterEntity Character { get; } = new(NullLoggerFactory.Instance,
            new Character { Id = 627u, Health = 100 }, new RegenConfiguration());
        public IWorldConnection Connection { get; } = Substitute.For<IWorldConnection>();
        public IMapInstance Instance { get; } = Substitute.For<IMapInstance>();
        public FakeTimeProvider Clock { get; } = new();
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        private readonly CastAbilityHandler _handler;

        public GodFixture()
        {
            Character.GodMode = true;
            Character.PowerType = PowerType.Mana;
            Character.CurrentPower = 100;
            Character.Spells.Load([]);
            Connection.Character.Returns(Character);
            Connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
            Instance.RunInstantAbility(default!, default, default!).ReturnsForAnyArgs(true);
            IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
            registry.GetInstanceById(Arg.Any<Guid>()).Returns(Instance);
            IWorld world = Substitute.For<IWorld>();
            world.InstanceRegistry.Returns(registry);
            _handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig(), Clock);
        }

        public IAbility GiveAbility(AbilityMetadata metadata)
        {
            IAbility ability = Substitute.For<IAbility>();
            ability.AbilityId.Returns(new AbilityId(1));
            ability.Metadata.Returns(metadata);
            Character.Spells.Load([ability]);
            return ability;
        }

        public void Cast(CCastAbilityPacket packet) => _handler.Execute(Connection, packet);

        public List<NetworkPacket> SentPackets() => Connection.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IWorldConnection.Send))
            .Select(call => (NetworkPacket)call.GetArguments()[0]!)
            .ToList();

        public SAbilityNotReadyPacket SingleRefusal() => Decode(Assert.Single(SentPackets()));
    }

    // ── Refusals before the instance ─────────────────────────────────────────

    [Fact]
    public void Answer_NotOwned_when_the_caster_does_not_have_the_ability()
    {
        var f = new Fixture();
        f.Character.Spells[Arg.Any<AbilityId>()].Returns((IAbility?)null);

        f.Cast(new CCastAbilityPacket { AbilityId = 99 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal(CastRejectReason.NotOwned, refusal.Reason);
        Assert.Equal(99u, refusal.AbilityId);
        Assert.Equal(0u, refusal.CooldownMs);
    }

    /// <summary>
    /// The time left goes out rounded up: a remainder under a millisecond is 1, never truncated to 0, since
    /// CooldownMs = 0 would tell the client the ability is ready when the server just refused it for cooling down.
    /// </summary>
    [Theory]
    [InlineData(1.5f, 1500u)]
    [InlineData(0.0004f, 1u)]
    public void Answer_Cooldown_with_the_time_left_rounded_up(float cooldown, uint expectedMs)
    {
        var f = new Fixture();
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" }).CooldownTimer.Returns(cooldown);

        f.Cast(new CCastAbilityPacket { AbilityId = 42 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal((CastRejectReason.Cooldown, 42u, expectedMs), (refusal.Reason, refusal.AbilityId, refusal.CooldownMs));
    }

    [Fact]
    public void Answer_Gcd_with_the_time_left_during_the_global_cooldown()
    {
        var f = new Fixture();
        f.Character.LastCastStartTime.Returns(f.Now.AddMilliseconds(-50));
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal((CastRejectReason.Gcd, new CombatConfig().GcdMs - 50), (refusal.Reason, refusal.CooldownMs));
    }

    /// <summary>
    /// The global cooldown runs on the container's clock (#793): an accepted cast starts it at that clock's now, a
    /// cast inside it is refused with the time left on that clock, and one once it has run out is taken.
    /// </summary>
    [Fact]
    public void Time_the_global_cooldown_by_the_containers_clock()
    {
        var f = new Fixture();
        uint gcdMs = new CombatConfig().GcdMs;
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });
        DateTime started = f.Now;

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });
        f.Character.Received(1).LastCastStartTime = started;

        f.Character.LastCastStartTime.Returns(started);
        f.Clock.Advance(TimeSpan.FromMilliseconds(gcdMs - 80));
        f.Cast(new CCastAbilityPacket { AbilityId = 1 });
        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal((CastRejectReason.Gcd, 80u), (refusal.Reason, refusal.CooldownMs));

        f.Clock.Advance(TimeSpan.FromMilliseconds(80));
        f.Cast(new CCastAbilityPacket { AbilityId = 1 });
        f.Instance.Received(2).RunInstantAbility(f.Character, Arg.Any<AbilityAim>(), ability);
        Assert.Single(f.SentPackets());
    }

    /// <summary>
    /// A requirement the Mana caster does not meet is refused with its reason and no time left, and nothing is
    /// dispatched. The power rule itself is AbilityCostShould's (#521, #652): a cost spent from another pool is
    /// WrongPowerType, and a cost whose row names no pool is a data fault the catalog refuses, so one that slips
    /// through is InternalError.
    /// </summary>
    [Theory]
    [InlineData(AbilityFlags.RequiresOutOfCombat, true, 100u, 0u, PowerType.None, CastRejectReason.RequiresOutOfCombat)]
    [InlineData(AbilityFlags.RequiresInCombat, false, 100u, 0u, PowerType.None, CastRejectReason.RequiresInCombat)]
    [InlineData(AbilityFlags.None, false, 5u, 30u, PowerType.Mana, CastRejectReason.NotEnoughPower)]
    [InlineData(AbilityFlags.None, false, 100u, 20u, PowerType.Fury, CastRejectReason.WrongPowerType)]
    [InlineData(AbilityFlags.None, false, 100u, 20u, PowerType.None, CastRejectReason.InternalError)]
    public void Refuse_a_requirement_the_caster_does_not_meet(AbilityFlags flags, bool inCombat, uint power, uint cost,
        PowerType costPool, CastRejectReason expected)
    {
        var f = new Fixture();
        f.Character.IsInCombat.Returns(inCombat);
        f.Character.CurrentPower.Returns((uint?)power);
        f.GiveAbility(new AbilityMetadata
        {
            Name = "X",
            ScriptName = "x",
            Flags = flags,
            Cost = cost,
            CostPowerType = costPool,
        });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal((expected, 0u), (refusal.Reason, refusal.CooldownMs));
        f.Instance.DidNotReceiveWithAnyArgs().RunInstantAbility(default!, default, default!);
        f.Instance.DidNotReceiveWithAnyArgs().QueueAbility(default!, default, default!);
        f.Character.DidNotReceive().MarkCombat();
    }

    [Fact]
    public void Answer_InternalError_when_the_casters_instance_is_gone()
    {
        var f = new Fixture();
        f.Registry.GetInstanceById(Arg.Any<Guid>()).Returns((IMapInstance?)null);
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        Assert.Equal(CastRejectReason.InternalError, f.SingleRefusal().Reason);
    }

    // ── One cast at a time (#521 item 4) ──────────────────────────────────────

    /// <summary>The refusal order: a dead caster is told Dead, even with a cast still in progress.</summary>
    [Fact]
    public void Answer_Dead_before_AlreadyCasting()
    {
        var f = new Fixture();
        f.Character.IsDead.Returns(true);
        f.Character.Spells.IsCasting.Returns(true);
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        Assert.Equal(CastRejectReason.Dead, f.SingleRefusal().Reason);
    }

    /// <summary>The refusal order: a cast in progress is AlreadyCasting, even inside the global cooldown.</summary>
    [Fact]
    public void Answer_AlreadyCasting_before_Gcd()
    {
        var f = new Fixture();
        f.Character.Spells.IsCasting.Returns(true);
        f.Character.LastCastStartTime.Returns(f.Now.AddMilliseconds(-50));
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal(CastRejectReason.AlreadyCasting, refusal.Reason);
        Assert.Equal(0u, refusal.CooldownMs);
    }

    // ── Aim (#164) ────────────────────────────────────────────────────────────

    public static TheoryData<Vector3Dto?> MissingAimPoints() => new()
    {
        null,
        new Vector3Dto { X = float.NaN, Y = 0f, Z = 1f },
        new Vector3Dto { X = 1f, Y = float.PositiveInfinity, Z = 1f },
        new Vector3Dto { X = 1f, Y = 0f, Z = float.NegativeInfinity },
    };

    [Theory]
    [MemberData(nameof(MissingAimPoints))]
    public void Answer_NoAimPoint_for_a_cursor_skill_without_a_finite_point(Vector3Dto? point)
    {
        var f = new Fixture();
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", AimMode = AbilityAimMode.Cursor });

        f.Cast(new CCastAbilityPacket { AbilityId = 1, GroundPos = point });

        Assert.Equal(CastRejectReason.NoAimPoint, f.SingleRefusal().Reason);
        f.Instance.DidNotReceiveWithAnyArgs().RunInstantAbility(default!, default, default!);
    }

    [Fact]
    public void Aim_a_cursor_skill_at_the_ground_point_and_ignore_TargetGuid()
    {
        var f = new Fixture();
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", AimMode = AbilityAimMode.Cursor });

        f.Cast(new CCastAbilityPacket { AbilityId = 1, TargetGuid = 12345, GroundPos = new Vector3Dto { X = 3f, Y = 9f, Z = 4f } });

        Assert.Empty(f.SentPackets());
        f.Instance.Received(1).RunInstantAbility(f.Character,
            Arg.Is<AbilityAim>(a => a.Point == new Vector3(3f, 9f, 4f)), ability);
    }

    /// <summary>#716: a Movement skill points from the caster toward the cursor, and carries no point of its own.</summary>
    [Fact]
    public void Aim_a_movement_skill_toward_the_ground_point_from_the_caster()
    {
        var f = new Fixture();
        f.Character.Orientation.Returns(new Vector3(0f, 90f, 0f));   // facing +X
        f.Character.Position.Returns(new Vector3(1f, 7f, 1f));
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });

        f.Cast(new CCastAbilityPacket { AbilityId = 1, GroundPos = new Vector3Dto { X = 4f, Y = 0f, Z = 5f } });

        Assert.Empty(f.SentPackets());
        f.Instance.Received(1).RunInstantAbility(f.Character,
            Arg.Is<AbilityAim>(a => a.Point == null && Math.Abs(a.Facing.x - 0.6f) < 1e-5f
                && a.Facing.y == 0f && Math.Abs(a.Facing.z - 0.8f) < 1e-5f), ability);
    }

    // ── Cast dispatch ────────────────────────────────────────────────────────

    /// <summary>An accepted cast goes down the path its cast time picks, is answered with nothing, and starts the global cooldown.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1.5f)]
    public void Dispatch_a_cast_on_the_path_its_cast_time_picks_and_send_no_refusal(float castTime)
    {
        var f = new Fixture();
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", CastTime = castTime });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        f.Instance.Received(castTime > 0 ? 0 : 1).RunInstantAbility(f.Character, Arg.Any<AbilityAim>(), ability);
        f.Instance.Received(castTime > 0 ? 1 : 0).QueueAbility(f.Character, Arg.Any<AbilityAim>(), ability);
        Assert.Empty(f.SentPackets());
        f.Character.Received().LastCastStartTime = Arg.Any<DateTime>();
    }

    /// <summary>A cast the cast system refuses, on either path, is InternalError.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1.5f)]
    public void Answer_InternalError_when_the_cast_system_refuses_the_cast(float castTime)
    {
        var f = new Fixture();
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", CastTime = castTime });
        f.Instance.QueueAbility(f.Character, Arg.Any<AbilityAim>(), ability).Returns(false);
        f.Instance.RunInstantAbility(f.Character, Arg.Any<AbilityAim>(), ability).Returns(false);

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        Assert.Equal(CastRejectReason.InternalError, f.SingleRefusal().Reason);
        // A refusal starts no global cooldown and does not put the caster in combat.
        f.Character.DidNotReceive().LastCastStartTime = Arg.Any<DateTime>();
        f.Character.DidNotReceive().MarkCombat();
    }

    // ── Wire ────────────────────────────────────────────────────────────────

    /// <summary>A payload from before #512 has no field 3, and must not decode as a real reason.</summary>
    [Fact]
    public void Decode_a_payload_without_a_reason_as_Unknown()
    {
        byte[] withoutReason = [0x08, 0x2a, 0x10, 0xac, 0x02]; // AbilityId 42, CooldownMs 300

        using var stream = new MemoryStream(withoutReason);
        SAbilityNotReadyPacket decoded = Serializer.Deserialize<SAbilityNotReadyPacket>(stream);

        Assert.Equal(42u, decoded.AbilityId);
        Assert.Equal(300u, decoded.CooldownMs);
        Assert.Equal(CastRejectReason.Unknown, decoded.Reason);
    }

    /// <summary>
    /// Payload bytes are unencrypted: FakeAvalonCryptoSession.Encrypt is a pass-through, so what
    /// SAbilityNotReadyPacket.Create wrote is exactly what protobuf-net reads back here.
    /// </summary>
    private static SAbilityNotReadyPacket Decode(NetworkPacket packet)
    {
        Assert.Equal(NetworkPacketType.SMSG_ABILITY_NOT_READY, packet.Header.Type);
        using var stream = new MemoryStream(packet.Payload);
        return Serializer.Deserialize<SAbilityNotReadyPacket>(stream);
    }

    /// <summary>
    /// A living Mana caster at the origin facing +Z (yaw 0), not casting, out of the GCD window on the fixture's
    /// clock (which stands still unless a test advances it) and with power to spare, in an instance the registry
    /// finds and whose cast system takes every cast. Each test changes only what it is about.
    /// </summary>
    private sealed class Fixture
    {
        public ICharacter Character { get; } = Substitute.For<ICharacter>();
        public IWorldConnection Connection { get; } = Substitute.For<IWorldConnection>();
        public IMapInstance Instance { get; } = Substitute.For<IMapInstance>();
        public IInstanceRegistry Registry { get; } = Substitute.For<IInstanceRegistry>();
        public FakeTimeProvider Clock { get; } = new();
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public CastAbilityHandler Handler { get; }

        public Fixture()
        {
            Character.IsDead.Returns(false);
            Character.LastCastStartTime.Returns(Now.AddSeconds(-10));
            Character.Position.Returns(Vector3.zero);
            Character.Orientation.Returns(Vector3.zero);
            Character.PowerType.Returns(PowerType.Mana);
            Character.CurrentPower.Returns((uint?)100);
            Character.Spells.IsCasting.Returns(false);

            Instance.RunInstantAbility(default!, default, default!).ReturnsForAnyArgs(true);
            Instance.QueueAbility(default!, default, default!).ReturnsForAnyArgs(true);
            Registry.GetInstanceById(Arg.Any<Guid>()).Returns(Instance);

            Connection.Character.Returns(Character);
            // NSubstitute cannot proxy ReadOnlySpan<byte> on IAvalonCryptoSession.Encrypt — use the concrete fake.
            Connection.CryptoSession.Returns(new FakeAvalonCryptoSession());

            IWorld world = Substitute.For<IWorld>();
            world.InstanceRegistry.Returns(Registry);
            Handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig(), Clock);
        }

        public void Cast(CCastAbilityPacket packet) => Handler.Execute(Connection, packet);

        /// <summary>A ready ability with this metadata, answered for any ability id.</summary>
        public IAbility GiveAbility(AbilityMetadata metadata)
        {
            IAbility ability = Substitute.For<IAbility>();
            ability.CooldownTimer.Returns(0f);
            ability.Metadata.Returns(metadata);
            Character.Spells[Arg.Any<AbilityId>()].Returns(ability);
            return ability;
        }

        public List<NetworkPacket> SentPackets() =>
            Connection.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IWorldConnection.Send))
                .Select(call => (NetworkPacket)call.GetArguments()[0]!)
                .ToList();

        /// <summary>Exactly one packet was sent, and it is a refusal; decoded.</summary>
        public SAbilityNotReadyPacket SingleRefusal() => Decode(Assert.Single(SentPackets()));
    }
}
