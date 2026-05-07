using System;
using System.Collections.Generic;
using System.Linq;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
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
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

public class CastAbilityHandlerShould
{
    [Fact]
    public void Should_reject_cast_when_dead()
    {
        var character = Substitute.For<ICharacter>();
        character.IsDead.Returns(true);

        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);

        var world = Substitute.For<IWorld>();
        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());

        var packet = new CCastAbilityPacket { AbilityId = 1, TargetGuid = 2 };
        handler.Execute(connection, packet);

        connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_reject_cast_when_character_is_null()
    {
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns((ICharacter?)null);

        var world = Substitute.For<IWorld>();
        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());

        var packet = new CCastAbilityPacket { AbilityId = 1 };
        handler.Execute(connection, packet);

        connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_drop_cast_when_ability_not_owned()
    {
        var (handler, connection, character, _) = BuildHandler();
        // Push LastCastStartTime well outside the GCD window so the GCD check passes.
        character.LastCastStartTime.Returns(DateTime.UtcNow.AddSeconds(-10));
        character.Spells[Arg.Any<AbilityId>()].Returns((IAbility?)null);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 99 });

        connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_send_SAbilityNotReadyPacket_when_on_cooldown()
    {
        var (handler, connection, character, _) = BuildHandler();
        // Push LastCastStartTime well outside the GCD window so the cooldown check is reached.
        character.LastCastStartTime.Returns(DateTime.UtcNow.AddSeconds(-10));

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(1500);
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 42 });

        connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_reject_cast_during_gcd_window()
    {
        var (handler, connection, character, _) = BuildHandler();
        character.LastCastStartTime.Returns(DateTime.UtcNow.AddMilliseconds(-50));

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(0);
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 1 });

        connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    // ── Combat-state gating ────────────────────────────────────────────────────

    [Fact]
    public void Should_reject_when_RequiresOutOfCombat_and_in_combat()
    {
        var (handler, connection, character, _) = BuildHandlerWithInstance();
        character.IsInCombat.Returns(true);

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(0);
        ability.Metadata.Returns(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Flags = AbilityFlags.RequiresOutOfCombat
        });
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 1 });

        connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_reject_when_RequiresInCombat_and_not_in_combat()
    {
        var (handler, connection, character, _) = BuildHandlerWithInstance();
        character.IsInCombat.Returns(false);

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(0);
        ability.Metadata.Returns(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Flags = AbilityFlags.RequiresInCombat
        });
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 1 });

        connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    // ── Cost ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Should_reject_when_insufficient_power()
    {
        var (handler, connection, character, _) = BuildHandlerWithInstance();
        character.CurrentPower.Returns((uint?)5);

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(0);
        ability.Metadata.Returns(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Cost = 30
        });
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 1 });

        connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    // ── Target / range / facing ──────────────────────────────────────────────

    [Fact]
    public void Should_reject_when_target_unit_not_found()
    {
        var (handler, connection, character, _) = BuildHandlerWithInstance(); // empty Characters/Creatures dicts
        character.CurrentPower.Returns((uint?)100);

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(0);
        ability.Metadata.Returns(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Cost = 0, Range = SpellRange.Medium
        });
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        // TargetGuid resolves to an unknown creature.
        var unknownGuid = new ObjectGuid(ObjectType.Creature, 999u);
        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 1, TargetGuid = unknownGuid.RawValue });

        // Drop without sending a packet (matches CharacterAttackHandler precedent).
        connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_reject_when_target_out_of_range()
    {
        // Place attacker at origin, target at (50, 0, 0). Ability range = Short (5).
        var attackerPos = new Vector3(0, 0, 0);
        var targetPos = new Vector3(50, 0, 0);

        var target = Substitute.For<ICreature>();
        target.Position.Returns(targetPos);
        var targetGuid = new ObjectGuid(ObjectType.Creature, 7u);
        target.Guid.Returns(targetGuid);

        var (handler, connection, character, _) = BuildHandlerWithInstance(creatures: new[] { target });
        character.Position.Returns(attackerPos);
        character.Orientation.Returns(new Vector3(0, 0, 0)); // facing +Z, target at +X — but we'll override facing below
        character.CurrentPower.Returns((uint?)100);

        // Ability requires facing — use orientation that points the attacker at target.
        // Target at +X requires orientation.y = 90 deg.
        character.Orientation.Returns(new Vector3(0, 90, 0));

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(0);
        ability.Metadata.Returns(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Cost = 0, Range = SpellRange.Short
        });
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 1, TargetGuid = targetGuid.RawValue });

        connection.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_reject_when_not_facing_target()
    {
        var attackerPos = new Vector3(0, 0, 0);
        var targetPos = new Vector3(0, 0, 5);

        var target = Substitute.For<ICreature>();
        target.Position.Returns(targetPos);
        var targetGuid = new ObjectGuid(ObjectType.Creature, 8u);
        target.Guid.Returns(targetGuid);

        var (handler, connection, character, _) = BuildHandlerWithInstance(creatures: new[] { target });
        character.Position.Returns(attackerPos);
        character.CurrentPower.Returns((uint?)100);
        // Orientation y = 180 deg → forward = (sin(180)=0, 0, cos(180)=-1). Target at +Z → 180 degree angle.
        character.Orientation.Returns(new Vector3(0, 180, 0));

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(0);
        ability.Metadata.Returns(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Cost = 0, Range = SpellRange.Medium
        });
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 1, TargetGuid = targetGuid.RawValue });

        // Match CharacterAttackHandler facing behavior: drop without packet.
        connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    // ── Cast dispatch ────────────────────────────────────────────────────────

    [Fact]
    public void Should_dispatch_instant_ability_when_cast_time_zero()
    {
        var attackerPos = new Vector3(0, 0, 0);
        var targetPos = new Vector3(0, 0, 1);

        var target = Substitute.For<ICreature>();
        target.Position.Returns(targetPos);
        var targetGuid = new ObjectGuid(ObjectType.Creature, 9u);
        target.Guid.Returns(targetGuid);

        var (handler, connection, character, context) =
            BuildHandlerWithInstance(creatures: new[] { target });
        character.Position.Returns(attackerPos);
        character.Orientation.Returns(new Vector3(0, 0, 0)); // facing +Z, target at +Z
        character.CurrentPower.Returns((uint?)100);

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(0);
        ability.Metadata.Returns(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Cost = 0, Range = SpellRange.Medium, CastTime = 0
        });
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 1, TargetGuid = targetGuid.RawValue });

        context.Received(1).RunInstantAbility(character, target, ability);
        context.DidNotReceive().QueueAbility(Arg.Any<ICharacter>(), Arg.Any<IUnit?>(), Arg.Any<IAbility>());
    }

    [Fact]
    public void Should_queue_cast_time_ability_when_cast_time_positive()
    {
        var attackerPos = new Vector3(0, 0, 0);
        var targetPos = new Vector3(0, 0, 1);

        var target = Substitute.For<ICreature>();
        target.Position.Returns(targetPos);
        var targetGuid = new ObjectGuid(ObjectType.Creature, 10u);
        target.Guid.Returns(targetGuid);

        var (handler, connection, character, context) =
            BuildHandlerWithInstance(creatures: new[] { target });
        character.Position.Returns(attackerPos);
        character.Orientation.Returns(new Vector3(0, 0, 0));
        character.CurrentPower.Returns((uint?)100);

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(0);
        ability.Metadata.Returns(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Cost = 0, Range = SpellRange.Medium, CastTime = 1.5f
        });
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        context.QueueAbility(character, target, ability).Returns(true);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 1, TargetGuid = targetGuid.RawValue });

        context.Received(1).QueueAbility(character, target, ability);
        context.Received(1).BroadcastUnitStartCast(character, 1.5f);
        context.DidNotReceive().RunInstantAbility(Arg.Any<IUnit>(), Arg.Any<IUnit?>(), Arg.Any<IAbility>());
    }

    [Fact]
    public void Should_set_LastCastStartTime_on_successful_cast()
    {
        var attackerPos = new Vector3(0, 0, 0);
        var targetPos = new Vector3(0, 0, 1);

        var target = Substitute.For<ICreature>();
        target.Position.Returns(targetPos);
        var targetGuid = new ObjectGuid(ObjectType.Creature, 11u);
        target.Guid.Returns(targetGuid);

        var (handler, connection, character, _) = BuildHandlerWithInstance(creatures: new[] { target });
        character.Position.Returns(attackerPos);
        character.Orientation.Returns(new Vector3(0, 0, 0));
        character.CurrentPower.Returns((uint?)100);

        var ability = Substitute.For<IAbility>();
        ability.CooldownTimer.Returns(0);
        ability.Metadata.Returns(new AbilityMetadata
        {
            Name = "X", ScriptName = "x", Cost = 0, Range = SpellRange.Medium, CastTime = 0
        });
        character.Spells[Arg.Any<AbilityId>()].Returns(ability);

        handler.Execute(connection, new CCastAbilityPacket { AbilityId = 1, TargetGuid = targetGuid.RawValue });

        character.Received().LastCastStartTime = Arg.Any<DateTime>();
    }

    private static (CastAbilityHandler handler, IWorldConnection connection, ICharacter character, IWorld world) BuildHandler()
    {
        var character = Substitute.For<ICharacter>();
        character.IsDead.Returns(false);
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        // NSubstitute cannot proxy ReadOnlySpan<byte> on IAvalonCryptoSession.Encrypt — use the concrete fake.
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        var world = Substitute.For<IWorld>();
        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());
        return (handler, connection, character, world);
    }

    /// <summary>
    /// Builds a handler wired to a substitute simulation context populated with the supplied
    /// creatures and characters. Used by the full-pipeline tests where the handler needs to
    /// resolve a live <see cref="ISimulationContext"/> via <c>world.InstanceRegistry</c>.
    /// </summary>
    private static (CastAbilityHandler handler, IWorldConnection connection, ICharacter character, IMapInstance context)
        BuildHandlerWithInstance(IEnumerable<ICreature>? creatures = null, IEnumerable<ICharacter>? characters = null)
    {
        var character = Substitute.For<ICharacter>();
        character.IsDead.Returns(false);
        // Push GCD outside the window by default so combat-pipeline tests reach the gating logic.
        character.LastCastStartTime.Returns(DateTime.UtcNow.AddSeconds(-10));

        var creatureDict = (creatures ?? Array.Empty<ICreature>()).ToDictionary(c => c.Guid);
        var characterDict = (characters ?? Array.Empty<ICharacter>()).ToDictionary(c => c.Guid);

        var instance = Substitute.For<IMapInstance>();
        instance.Creatures.Returns(creatureDict);
        instance.Characters.Returns(characterDict);

        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());

        var registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(Arg.Any<Guid>()).Returns(instance);
        var world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);

        var handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());
        return (handler, connection, character, instance);
    }
}
