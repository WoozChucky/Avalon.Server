using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// Every refused cast is answered with exactly one SAbilityNotReadyPacket naming its reason (#512);
/// only a connection with no character hears nothing. The facing cone is read from CombatConfig
/// (#513), and compared strictly.
/// </summary>
public class CastAbilityHandlerShould
{
    // ── Refusals before the instance ─────────────────────────────────────────

    [Fact]
    public void Answer_Dead_when_the_caster_is_dead()
    {
        var f = new Fixture();
        f.Character.IsDead.Returns(true);

        f.Cast(new CCastAbilityPacket { AbilityId = 1, TargetGuid = 2 });

        Assert.Equal(CastRejectReason.Dead, f.SingleRefusal().Reason);
    }

    [Fact]
    public void Send_nothing_when_the_connection_has_no_character()
    {
        var f = new Fixture();
        f.Connection.Character.Returns((ICharacter?)null);

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        Assert.Empty(f.SentPackets());
    }

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

    [Fact]
    public void Answer_Cooldown_with_the_time_left_when_the_ability_is_cooling_down()
    {
        var f = new Fixture();
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" }).CooldownTimer.Returns(1.5f);

        f.Cast(new CCastAbilityPacket { AbilityId = 42 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal(CastRejectReason.Cooldown, refusal.Reason);
        Assert.Equal(42u, refusal.AbilityId);
        Assert.Equal(1500u, refusal.CooldownMs);
    }

    [Fact]
    public void Answer_Gcd_with_the_time_left_during_the_global_cooldown()
    {
        var f = new Fixture();
        f.Character.LastCastStartTime.Returns(DateTime.UtcNow.AddMilliseconds(-50));
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal(CastRejectReason.Gcd, refusal.Reason);
        Assert.InRange(refusal.CooldownMs, 1u, new CombatConfig().GcdMs);
    }

    [Fact]
    public void Answer_RequiresOutOfCombat_when_an_out_of_combat_ability_is_cast_in_combat()
    {
        var f = new Fixture();
        f.Character.IsInCombat.Returns(true);
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Flags = AbilityFlags.RequiresOutOfCombat });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal(CastRejectReason.RequiresOutOfCombat, refusal.Reason);
        Assert.Equal(0u, refusal.CooldownMs);
    }

    [Fact]
    public void Answer_RequiresInCombat_when_an_in_combat_ability_is_cast_out_of_combat()
    {
        var f = new Fixture();
        f.Character.IsInCombat.Returns(false);
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Flags = AbilityFlags.RequiresInCombat });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal(CastRejectReason.RequiresInCombat, refusal.Reason);
        Assert.Equal(0u, refusal.CooldownMs);
    }

    [Fact]
    public void Answer_NotEnoughPower_when_the_caster_cannot_pay()
    {
        var f = new Fixture();
        f.Character.CurrentPower.Returns((uint?)5);
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Cost = 30 });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal(CastRejectReason.NotEnoughPower, refusal.Reason);
        Assert.Equal(0u, refusal.CooldownMs);
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

    // ── Target / facing / range ─────────────────────────────────────────────

    [Fact]
    public void Answer_TargetNotFound_when_the_target_is_not_in_the_instance()
    {
        var f = new Fixture();
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Range = SpellRange.Medium });

        var unknownGuid = new ObjectGuid(ObjectType.Creature, 999u);
        f.Cast(new CCastAbilityPacket { AbilityId = 1, TargetGuid = unknownGuid.RawValue });

        Assert.Equal(CastRejectReason.TargetNotFound, f.SingleRefusal().Reason);
    }

    [Fact]
    public void Answer_OutOfRange_when_the_target_is_too_far()
    {
        var f = new Fixture();
        ObjectGuid target = f.AddTarget(new Vector3(50, 0, 0));
        f.Character.Orientation.Returns(new Vector3(0, 90, 0)); // facing +X, at the target
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Range = SpellRange.Short });

        f.Cast(new CCastAbilityPacket { AbilityId = 1, TargetGuid = target.RawValue });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal(CastRejectReason.OutOfRange, refusal.Reason);
        Assert.Equal(0u, refusal.CooldownMs);
    }

    [Fact]
    public void Answer_NotFacing_when_the_caster_faces_away_from_the_target()
    {
        var f = new Fixture();
        ObjectGuid target = f.AddTarget(new Vector3(0, 0, 5));
        f.Character.Orientation.Returns(new Vector3(0, 180, 0)); // facing -Z, target at +Z
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Range = SpellRange.Medium });

        f.Cast(new CCastAbilityPacket { AbilityId = 1, TargetGuid = target.RawValue });

        Assert.Equal(CastRejectReason.NotFacing, f.SingleRefusal().Reason);
    }

    /// <summary>
    /// Yaw 0 faces exactly +Z and a target on +X lies exactly along +X, so the angle the handler
    /// measures is exactly Vector3.Angle(forward, right). A cone of exactly that angle refuses the
    /// cast (strict less-than); the next float up accepts it. Neither is the default 65, so this
    /// also proves the handler reads CombatConfig, not a constant.
    /// </summary>
    [Fact]
    public void Refuse_a_target_exactly_on_the_configured_cone_edge_and_accept_one_just_inside()
    {
        float edge = Vector3.Angle(Vector3.forward, Vector3.right);

        var onEdge = new Fixture(new CombatConfig { MaxFacingAngleDeg = edge });
        onEdge.CastSideways();
        Assert.Equal(CastRejectReason.NotFacing, onEdge.SingleRefusal().Reason);

        var inside = new Fixture(new CombatConfig { MaxFacingAngleDeg = MathF.BitIncrement(edge) });
        inside.CastSideways();
        Assert.Empty(inside.SentPackets());
        inside.Instance.Received(1).RunInstantAbility(inside.Character, Arg.Any<IUnit?>(), Arg.Any<IAbility>());
    }

    [Theory]
    [InlineData(64.9f, true)]
    [InlineData(65.1f, false)]
    public void Use_a_65_degree_cone_by_default(float yawToTarget, bool accepted)
    {
        var f = new Fixture();
        float radians = yawToTarget * Mathf.Deg2Rad;
        ObjectGuid target = f.AddTarget(new Vector3(MathF.Sin(radians), 0, MathF.Cos(radians)));
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Range = SpellRange.Medium });

        f.Cast(new CCastAbilityPacket { AbilityId = 1, TargetGuid = target.RawValue });

        if (accepted)
            Assert.Empty(f.SentPackets());
        else
            Assert.Equal(CastRejectReason.NotFacing, f.SingleRefusal().Reason);
    }

    // ── Cast dispatch ────────────────────────────────────────────────────────

    [Fact]
    public void Dispatch_an_instant_ability_and_send_no_refusal()
    {
        var f = new Fixture();
        ObjectGuid targetGuid = f.AddTarget(new Vector3(0, 0, 1)); // facing +Z, target at +Z
        IAbility ability = f.GiveAbility(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Range = SpellRange.Medium, CastTime = 0
        });

        f.Cast(new CCastAbilityPacket { AbilityId = 1, TargetGuid = targetGuid.RawValue });

        f.Instance.Received(1).RunInstantAbility(f.Character, f.Creature(targetGuid), ability);
        f.Instance.DidNotReceive().QueueAbility(Arg.Any<ICharacter>(), Arg.Any<IUnit?>(), Arg.Any<IAbility>());
        Assert.Empty(f.SentPackets());
    }

    [Fact]
    public void Queue_a_cast_time_ability_and_send_no_refusal()
    {
        var f = new Fixture();
        ObjectGuid targetGuid = f.AddTarget(new Vector3(0, 0, 1));
        IAbility ability = f.GiveAbility(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Range = SpellRange.Medium, CastTime = 1.5f
        });
        ICreature target = f.Creature(targetGuid);
        f.Instance.QueueAbility(f.Character, target, ability).Returns(true);

        f.Cast(new CCastAbilityPacket { AbilityId = 1, TargetGuid = targetGuid.RawValue });

        f.Instance.Received(1).QueueAbility(f.Character, target, ability);
        f.Instance.Received(1).BroadcastUnitStartCast(f.Character, 1.5f);
        f.Instance.DidNotReceive().RunInstantAbility(Arg.Any<IUnit>(), Arg.Any<IUnit?>(), Arg.Any<IAbility>());
        Assert.Empty(f.SentPackets());
    }

    [Fact]
    public void Answer_InternalError_when_the_queue_refuses_the_cast()
    {
        var f = new Fixture();
        ObjectGuid targetGuid = f.AddTarget(new Vector3(0, 0, 1));
        IAbility ability = f.GiveAbility(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Range = SpellRange.Medium, CastTime = 1.5f
        });
        f.Instance.QueueAbility(f.Character, f.Creature(targetGuid), ability).Returns(false);

        f.Cast(new CCastAbilityPacket { AbilityId = 1, TargetGuid = targetGuid.RawValue });

        Assert.Equal(CastRejectReason.InternalError, f.SingleRefusal().Reason);
        f.Instance.DidNotReceive().BroadcastUnitStartCast(Arg.Any<IUnit>(), Arg.Any<float>());
    }

    [Fact]
    public void Set_LastCastStartTime_on_a_successful_cast()
    {
        var f = new Fixture();
        ObjectGuid targetGuid = f.AddTarget(new Vector3(0, 0, 1));
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Range = SpellRange.Medium, CastTime = 0 });

        f.Cast(new CCastAbilityPacket { AbilityId = 1, TargetGuid = targetGuid.RawValue });

        f.Character.Received().LastCastStartTime = Arg.Any<DateTime>();
    }

    // ── Wire ────────────────────────────────────────────────────────────────

    [Fact]
    public void Carry_the_reason_through_a_protobuf_round_trip()
    {
        NetworkPacket sent = SAbilityNotReadyPacket.Create(7, CastRejectReason.NotFacing, 0u,
            new FakeAvalonCryptoSession().Encrypt);

        SAbilityNotReadyPacket decoded = Decode(sent);

        Assert.Equal(7u, decoded.AbilityId);
        Assert.Equal(CastRejectReason.NotFacing, decoded.Reason);
    }

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
    /// A living caster at the origin facing +Z (yaw 0), out of the GCD window and with power to
    /// spare, in an instance the registry finds. Each test changes only what it is about.
    /// </summary>
    private sealed class Fixture
    {
        private readonly Dictionary<ObjectGuid, ICreature> _creatures = new();
        private uint _nextCreatureId = 100;

        public ICharacter Character { get; } = Substitute.For<ICharacter>();
        public IWorldConnection Connection { get; } = Substitute.For<IWorldConnection>();
        public IMapInstance Instance { get; } = Substitute.For<IMapInstance>();
        public IInstanceRegistry Registry { get; } = Substitute.For<IInstanceRegistry>();
        public CastAbilityHandler Handler { get; }

        public Fixture(CombatConfig? config = null)
        {
            Character.IsDead.Returns(false);
            Character.LastCastStartTime.Returns(DateTime.UtcNow.AddSeconds(-10));
            Character.Position.Returns(Vector3.zero);
            Character.Orientation.Returns(Vector3.zero);
            Character.CurrentPower.Returns((uint?)100);

            Instance.Creatures.Returns(_creatures);
            Instance.Characters.Returns(new Dictionary<ObjectGuid, ICharacter>());
            Registry.GetInstanceById(Arg.Any<Guid>()).Returns(Instance);

            Connection.Character.Returns(Character);
            // NSubstitute cannot proxy ReadOnlySpan<byte> on IAvalonCryptoSession.Encrypt — use the concrete fake.
            Connection.CryptoSession.Returns(new FakeAvalonCryptoSession());

            var world = Substitute.For<IWorld>();
            world.InstanceRegistry.Returns(Registry);
            Handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, config ?? new CombatConfig());
        }

        public void Cast(CCastAbilityPacket packet) => Handler.Execute(Connection, packet);

        /// <summary>A ready ability with this metadata, answered for any ability id.</summary>
        public IAbility GiveAbility(AbilityMetadata metadata)
        {
            var ability = Substitute.For<IAbility>();
            ability.CooldownTimer.Returns(0f);
            ability.Metadata.Returns(metadata);
            Character.Spells[Arg.Any<AbilityId>()].Returns(ability);
            return ability;
        }

        public ObjectGuid AddTarget(Vector3 position)
        {
            var guid = new ObjectGuid(ObjectType.Creature, _nextCreatureId++);
            var creature = Substitute.For<ICreature>();
            creature.Guid.Returns(guid);
            creature.Position.Returns(position);
            _creatures[guid] = creature;
            return guid;
        }

        /// <summary>
        /// The creature behind a guid, read from the fixture's own dictionary: reading it through
        /// the substitute instance inside a Received() or Returns() call would break NSubstitute.
        /// </summary>
        public ICreature Creature(ObjectGuid guid) => _creatures[guid];

        /// <summary>An instant cast at a target 1 m along +X, square to the caster's +Z facing.</summary>
        public void CastSideways()
        {
            ObjectGuid target = AddTarget(Vector3.right);
            GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Range = SpellRange.Medium });
            Cast(new CCastAbilityPacket { AbilityId = 1, TargetGuid = target.RawValue });
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
