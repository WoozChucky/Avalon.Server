using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalon.Combat;
using Avalon.Common;
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
/// only a connection with no character hears nothing. A cast aims at a direction or a ground point,
/// never at a unit (#164): TargetGuid is ignored, and there is no range or facing check.
/// </summary>
public class CastAbilityHandlerShould
{
    [Fact]
    public void Admit_a_god_mode_cast_despite_gcd_ability_cooldown_and_no_power()
    {
        var f = new GodFixture();
        f.Character.LastCastStartTime = DateTime.UtcNow;
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
    public void Restore_the_previous_gcd_and_ability_cooldown_when_god_mode_ends()
    {
        var f = new GodFixture();
        f.Character.LastCastStartTime = DateTime.UtcNow;
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });
        ability.CooldownTimer.Returns(5f);
        f.Character.GodMode = false;

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });
        Assert.Equal(CastRejectReason.Gcd, f.SingleRefusal().Reason);

        f.Connection.ClearReceivedCalls();
        f.Character.LastCastStartTime = DateTime.UtcNow.AddSeconds(-10);
        f.Cast(new CCastAbilityPacket { AbilityId = 1 });
        Assert.Equal(CastRejectReason.Cooldown, f.SingleRefusal().Reason);
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
            var registry = Substitute.For<IInstanceRegistry>();
            registry.GetInstanceById(Arg.Any<Guid>()).Returns(Instance);
            var world = Substitute.For<IWorld>();
            world.InstanceRegistry.Returns(registry);
            _handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());
        }

        public IAbility GiveAbility(AbilityMetadata metadata)
        {
            var ability = Substitute.For<IAbility>();
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

    /// <summary>
    /// A remainder under a millisecond is rounded up, never truncated to 0: CooldownMs = 0 would
    /// tell the client the ability is ready when the server just refused it for cooling down.
    /// </summary>
    [Fact]
    public void Round_a_sub_millisecond_cooldown_up_to_1()
    {
        var f = new Fixture();
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" }).CooldownTimer.Returns(0.0004f);

        f.Cast(new CCastAbilityPacket { AbilityId = 42 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal(CastRejectReason.Cooldown, refusal.Reason);
        Assert.Equal(1u, refusal.CooldownMs);
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
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Cost = 30, CostPowerType = PowerType.Mana });

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

    // ── One cast at a time (#521 item 4) ──────────────────────────────────────

    [Fact]
    public void Answer_AlreadyCasting_while_another_cast_is_in_progress()
    {
        var f = new Fixture();
        f.Character.Spells.IsCasting.Returns(true);
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        Assert.Equal(CastRejectReason.AlreadyCasting, f.SingleRefusal().Reason);
        f.Instance.DidNotReceiveWithAnyArgs().RunInstantAbility(default!, default, default!);
        f.Instance.DidNotReceiveWithAnyArgs().QueueAbility(default!, default, default!);
    }

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
        f.Character.LastCastStartTime.Returns(DateTime.UtcNow.AddMilliseconds(-50));
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

    [Fact]
    public void Aim_a_movement_skill_along_the_casters_facing_without_a_point()
    {
        var f = new Fixture();
        f.Character.Orientation.Returns(new Vector3(0f, 90f, 0f));
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });

        f.Cast(new CCastAbilityPacket { AbilityId = 1, GroundPos = new Vector3Dto { X = 3f, Y = 0f, Z = 4f } });

        f.Instance.Received(1).RunInstantAbility(f.Character,
            Arg.Is<AbilityAim>(a => a.Point == null && Math.Abs(a.Facing.x - 1f) < 1e-4f), ability);
    }

    /// <summary>A queued cast is aimed once, when it starts: the aim travels with it into the queue.</summary>
    [Fact]
    public void Queue_a_cast_time_cursor_skill_with_the_aim_it_started_with()
    {
        var f = new Fixture();
        IAbility ability = f.GiveAbility(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", AimMode = AbilityAimMode.Cursor, CastTime = 1f,
        });

        f.Cast(new CCastAbilityPacket { AbilityId = 1, GroundPos = new Vector3Dto { X = 5f, Y = 0f, Z = 6f } });

        f.Instance.Received(1).QueueAbility(f.Character,
            Arg.Is<AbilityAim>(a => a.Point == new Vector3(5f, 0f, 6f) && Math.Abs(a.Facing.z - 1f) < 1e-4f), ability);
    }

    // ── The power rule (#521 item 2) ──────────────────────────────────────────

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Answer_WrongPowerType_on_both_paths_for_a_cost_on_a_caster_without_a_pool(float castTime)
    {
        var f = new Fixture();
        f.Character.PowerType.Returns(PowerType.None);
        f.GiveAbility(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Cost = 10, CostPowerType = PowerType.Mana, CastTime = castTime,
        });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        Assert.Equal(CastRejectReason.WrongPowerType, f.SingleRefusal().Reason);
        f.Instance.DidNotReceiveWithAnyArgs().RunInstantAbility(default!, default, default!);
        f.Instance.DidNotReceiveWithAnyArgs().QueueAbility(default!, default, default!);
    }

    /// <summary>Fury is spendable like Mana and Energy (#526).</summary>
    [Fact]
    public void Dispatch_a_Fury_cast_the_pool_can_pay()
    {
        var f = new Fixture();
        f.Character.PowerType.Returns(PowerType.Fury);
        f.Character.CurrentPower.Returns((uint?)20);
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Cost = 20, CostPowerType = PowerType.Fury });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        Assert.Empty(f.SentPackets());
        f.Instance.Received(1).RunInstantAbility(f.Character, Arg.Any<AbilityAim>(), ability);
    }

    [Fact]
    public void Answer_NotEnoughPower_for_a_Fury_cast_the_pool_cannot_pay()
    {
        var f = new Fixture();
        f.Character.PowerType.Returns(PowerType.Fury);
        f.Character.CurrentPower.Returns((uint?)19);
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Cost = 20, CostPowerType = PowerType.Fury });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        Assert.Equal(CastRejectReason.NotEnoughPower, f.SingleRefusal().Reason);
        f.Instance.DidNotReceiveWithAnyArgs().RunInstantAbility(default!, default, default!);
    }

    /// <summary>
    /// #652: a cost is spent from the pool the ability names. A Warrior given a Mana ability is refused with a reason
    /// the client can show, on both paths, however much Fury it holds, and nothing is dispatched.
    /// </summary>
    [Theory]
    [InlineData(PowerType.Fury, PowerType.Mana, 0f)]
    [InlineData(PowerType.Fury, PowerType.Mana, 1f)]
    [InlineData(PowerType.Mana, PowerType.Fury, 0f)]
    [InlineData(PowerType.Energy, PowerType.Mana, 1f)]
    [InlineData(PowerType.Mana, PowerType.Energy, 0f)]
    public void Answer_WrongPowerType_for_a_cost_spent_from_another_pool(PowerType casterPool, PowerType costPool, float castTime)
    {
        var f = new Fixture();
        f.Character.PowerType.Returns(casterPool);
        f.Character.CurrentPower.Returns((uint?)100);
        f.GiveAbility(new AbilityMetadata
        {
            Name = "Flame Surge", ScriptName = "x", Cost = 20, CostPowerType = costPool, CastTime = castTime,
        });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        SAbilityNotReadyPacket refusal = f.SingleRefusal();
        Assert.Equal(CastRejectReason.WrongPowerType, refusal.Reason);
        Assert.Equal(0u, refusal.CooldownMs);
        f.Instance.DidNotReceiveWithAnyArgs().RunInstantAbility(default!, default, default!);
        f.Instance.DidNotReceiveWithAnyArgs().QueueAbility(default!, default, default!);
        f.Character.DidNotReceive().MarkCombat();
    }

    /// <summary>A row with a cost and no pool is a data fault the catalog refuses; one that slips through is InternalError.</summary>
    [Fact]
    public void Answer_InternalError_for_a_cost_that_names_no_pool()
    {
        var f = new Fixture();
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", Cost = 20 });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        Assert.Equal(CastRejectReason.InternalError, f.SingleRefusal().Reason);
        f.Instance.DidNotReceiveWithAnyArgs().RunInstantAbility(default!, default, default!);
    }

    // ── Cast dispatch ────────────────────────────────────────────────────────

    [Fact]
    public void Dispatch_an_instant_ability_and_send_no_refusal()
    {
        var f = new Fixture();
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", CastTime = 0 });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        f.Instance.Received(1).RunInstantAbility(f.Character, Arg.Any<AbilityAim>(), ability);
        f.Instance.DidNotReceiveWithAnyArgs().QueueAbility(default!, default, default!);
        Assert.Empty(f.SentPackets());
    }

    [Fact]
    public void Queue_a_cast_time_ability_and_send_no_refusal()
    {
        var f = new Fixture();
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", CastTime = 1.5f });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        f.Instance.Received(1).QueueAbility(f.Character, Arg.Any<AbilityAim>(), ability);
        f.Instance.DidNotReceiveWithAnyArgs().RunInstantAbility(default!, default, default!);
        Assert.Empty(f.SentPackets());
    }

    [Fact]
    public void Answer_InternalError_when_the_queue_refuses_the_cast()
    {
        var f = new Fixture();
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", CastTime = 1.5f });
        f.Instance.QueueAbility(f.Character, Arg.Any<AbilityAim>(), ability).Returns(false);

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        Assert.Equal(CastRejectReason.InternalError, f.SingleRefusal().Reason);
        // A refusal starts no global cooldown and does not put the caster in combat.
        f.Character.DidNotReceive().LastCastStartTime = Arg.Any<DateTime>();
        f.Character.DidNotReceive().MarkCombat();
    }

    [Fact]
    public void Answer_InternalError_when_the_instant_path_refuses_the_cast()
    {
        var f = new Fixture();
        IAbility ability = f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x" });
        f.Instance.RunInstantAbility(f.Character, Arg.Any<AbilityAim>(), ability).Returns(false);

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        Assert.Equal(CastRejectReason.InternalError, f.SingleRefusal().Reason);
        f.Character.DidNotReceive().LastCastStartTime = Arg.Any<DateTime>();
        f.Character.DidNotReceive().MarkCombat();
    }

    [Fact]
    public void Set_LastCastStartTime_on_a_successful_cast()
    {
        var f = new Fixture();
        f.GiveAbility(new AbilityMetadata { Name = "X", ScriptName = "x", CastTime = 0 });

        f.Cast(new CCastAbilityPacket { AbilityId = 1 });

        f.Character.Received().LastCastStartTime = Arg.Any<DateTime>();
    }

    // ── Wire ────────────────────────────────────────────────────────────────

    [Fact]
    public void Carry_the_reason_through_a_protobuf_round_trip()
    {
        NetworkPacket sent = SAbilityNotReadyPacket.Create(7, CastRejectReason.Cooldown, 1234u,
            new FakeAvalonCryptoSession().Encrypt);

        SAbilityNotReadyPacket decoded = Decode(sent);

        Assert.Equal(7u, decoded.AbilityId);
        Assert.Equal(1234u, decoded.CooldownMs);
        Assert.Equal(CastRejectReason.Cooldown, decoded.Reason);
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
    /// A living Mana caster at the origin facing +Z (yaw 0), not casting, out of the GCD window and
    /// with power to spare, in an instance the registry finds and whose cast system takes every cast.
    /// Each test changes only what it is about.
    /// </summary>
    private sealed class Fixture
    {
        public ICharacter Character { get; } = Substitute.For<ICharacter>();
        public IWorldConnection Connection { get; } = Substitute.For<IWorldConnection>();
        public IMapInstance Instance { get; } = Substitute.For<IMapInstance>();
        public IInstanceRegistry Registry { get; } = Substitute.For<IInstanceRegistry>();
        public CastAbilityHandler Handler { get; }

        public Fixture()
        {
            Character.IsDead.Returns(false);
            Character.LastCastStartTime.Returns(DateTime.UtcNow.AddSeconds(-10));
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

            var world = Substitute.For<IWorld>();
            world.InstanceRegistry.Returns(Registry);
            Handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());
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

        public List<NetworkPacket> SentPackets() =>
            Connection.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IWorldConnection.Send))
                .Select(call => (NetworkPacket)call.GetArguments()[0]!)
                .ToList();

        /// <summary>Exactly one packet was sent, and it is a refusal; decoded.</summary>
        public SAbilityNotReadyPacket SingleRefusal() => Decode(Assert.Single(SentPackets()));
    }
}
