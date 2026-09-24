using System;
using Avalon.Common.Mathematics;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Scripts;

public class CreatureCombatScriptShould
{
    [Fact]
    public void Chase_By_Setting_A_Destination_Rather_Than_Moving_Itself()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        (CreatureCombatScript script, ICreature creature, ICharacter target) =
            BuildChasingScript(locomotion, targetAt: new Vector3(20f, 0f, 0f));

        script.Update(TimeSpan.FromSeconds(0.1));

        locomotion.ReceivedWithAnyArgs().MoveTo(default!, default);
        creature.DidNotReceive().Position = Arg.Any<Vector3>();
    }

    /// <summary>
    /// A leash-return is a placement, not a journey. Under a crowd, writing Position directly would
    /// leave the agent behind and the creature would be dragged back next tick.
    /// </summary>
    [Fact]
    public void Teleport_Rather_Than_Walk_When_Snapping_Home()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        (CreatureCombatScript script, ICreature creature, _) =
            BuildScriptReturningHome(locomotion, home: new Vector3(1f, 0f, 1f));

        script.Update(TimeSpan.FromSeconds(0.1));

        locomotion.Received().Teleport(creature, new Vector3(1f, 0f, 1f));
        creature.DidNotReceive().Position = Arg.Any<Vector3>();
    }

    /// <summary>
    /// Regression: a mocked <see cref="ICreatureLocomotion"/> can't catch this — the bug is in
    /// the interaction between the script and a REAL locomotion's Advance, which
    /// <c>MapInstance.Update</c> ticks after every AI script every frame regardless of what the
    /// script decided. If the attack-range branch stops setting Velocity/MoveState directly
    /// (as a no-op against a path that's still loaded) instead of calling
    /// <see cref="ICreatureLocomotion.Stop"/>, WaypointLocomotion.Advance walks the creature
    /// along the stale path on this exact tick, straight through the target's centre.
    /// </summary>
    [Fact]
    public void Stop_The_Real_Locomotion_When_Entering_Attack_Range()
    {
        var navigator = Substitute.For<IMapNavigator>();
        var locomotion = new WaypointLocomotion(_ => navigator);

        ICreature creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new Avalon.Common.ObjectGuid(Avalon.Common.ObjectType.Creature, 1));
        creature.TauntedBy      = null;
        creature.TauntExpiresAt = DateTime.MinValue;
        creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());
        creature.Speed.Returns(4f);
        creature.Position.Returns(Vector3.zero);

        var targetPosition = new Vector3(2f, 0f, 0f);
        ICharacter target = Substitute.For<ICharacter>();
        target.Position.Returns(targetPosition);
        target.IsDead.Returns(false);

        var combat = Substitute.For<ICombatService>();
        combat.GetEncounterFor(creature).Returns((IEncounter?)null);

        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);
        context.Locomotion.Returns(locomotion);

        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context);
        script.OnEnteredRange(target); // _initialPosition = Vector3.zero, State = Combat

        // A prior chase tick already loaded a real path whose final (only) waypoint is the
        // target's exact centre — the scenario the bug report describes.
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns([targetPosition]);
        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, targetPosition);

        // Now within AttackRange (1.5f) of the target but not yet standing on the waypoint
        // (WaypointLocomotion's 0.1f arrival threshold) — if the script fails to stop the
        // locomotion, Advance below still has a waypoint to walk towards.
        creature.Position.Returns(new Vector3(1f, 0f, 0f));

        script.Update(TimeSpan.FromSeconds(0.1));      // AI scripts tick first...
        locomotion.Update(TimeSpan.FromSeconds(0.1));  // ...then locomotion, same as MapInstance.Update.

        creature.DidNotReceive().Position = Arg.Any<Vector3>();
    }

    /// <summary>
    /// Regression: a creature leashed while already standing within 0.1f of spawn never gets a
    /// chance to naturally come to rest (no waypoint is consumed that tick, since none was ever
    /// walked) — ResetToIdleAtSpawn is the only thing left that can stop it. Without this, the
    /// creature keeps whatever destination/MoveState it had and the client extrapolates a
    /// creature whose position never changes — the exact drift the original code's comment
    /// warned about.
    /// </summary>
    [Fact]
    public void Stop_Locomotion_When_Resetting_To_Idle_At_Spawn()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();

        ICreature creature = Substitute.For<ICreature>();
        creature.Position.Returns(new Vector3(5f, 0f, 5f));
        creature.TauntedBy      = null;
        creature.TauntExpiresAt = DateTime.MinValue;
        creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());

        var combat = Substitute.For<ICombatService>();
        combat.GetEncounterFor(creature).Returns((IEncounter?)null);

        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);
        context.Locomotion.Returns(locomotion);

        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context);

        ICharacter target = Substitute.For<ICharacter>();
        target.IsDead.Returns(true);
        script.OnEnteredRange(target); // _initialPosition = (5, 0, 5)

        script.Update(TimeSpan.FromSeconds(0.1)); // target is dead -> State = Returning
        locomotion.ClearReceivedCalls();

        // Still standing on spawn (distance to _initialPosition < 0.1f) on the very next tick —
        // hits the early ResetToIdleAtSpawn() branch without ever calling MoveTo/Teleport again.
        script.Update(TimeSpan.FromSeconds(0.1));

        locomotion.Received().Stop(creature);
    }

    [Fact]
    public void Should_pick_top_threat_attacker_as_target()
    {
        var (script, encounter, _) = BuildScript(out var creature);

        ICharacter attacker = Substitute.For<ICharacter>();
        encounter.GetTopThreat(creature).Returns(attacker);

        IUnit? picked = script.PickTarget();

        Assert.Same(attacker, picked);
    }

    [Fact]
    public void Should_return_null_when_no_encounter_exists_and_no_taunt()
    {
        var (script, _, combat) = BuildScript(out var creature);

        // No encounter for this creature.
        combat.GetEncounterFor(creature).Returns((IEncounter?)null);

        IUnit? picked = script.PickTarget();

        Assert.Null(picked);
    }

    [Fact]
    public void Should_honor_taunt_until_expiry()
    {
        var (script, encounter, _) = BuildScript(out var creature);

        ICharacter tank = Substitute.For<ICharacter>();
        ICharacter dps  = Substitute.For<ICharacter>();

        // DPS would normally be top-threat, but tank has an active taunt.
        encounter.GetTopThreat(creature).Returns(dps);
        creature.TauntedBy      = tank;
        creature.TauntExpiresAt = DateTime.UtcNow.AddSeconds(5);

        IUnit? picked = script.PickTarget();

        Assert.Same(tank, picked);
    }

    [Fact]
    public void Should_revert_to_top_threat_after_taunt_expires()
    {
        var (script, encounter, _) = BuildScript(out var creature);

        ICharacter tank = Substitute.For<ICharacter>();
        ICharacter dps  = Substitute.For<ICharacter>();

        encounter.GetTopThreat(creature).Returns(dps);

        // Taunt that has already expired.
        creature.TauntedBy      = tank;
        creature.TauntExpiresAt = DateTime.UtcNow.AddSeconds(-1);

        IUnit? picked = script.PickTarget();

        Assert.Same(dps, picked);
    }

    [Fact]
    public void Should_prefer_taunter_over_top_threat_even_when_threat_higher()
    {
        // Taunt is an authoritative override, not a tiebreaker. Even if GetTopThreat returns a
        // unit that isn't the taunter (e.g. the threat list disagrees with the taunt due to
        // floating-point edge cases), the taunter wins while the taunt is active.
        var (script, encounter, _) = BuildScript(out var creature);

        ICharacter tank      = Substitute.For<ICharacter>();
        ICharacter someoneElse = Substitute.For<ICharacter>();

        encounter.GetTopThreat(creature).Returns(someoneElse);
        creature.TauntedBy      = tank;
        creature.TauntExpiresAt = DateTime.UtcNow.AddSeconds(2);

        IUnit? picked = script.PickTarget();

        Assert.Same(tank, picked);
    }

    private static (CreatureCombatScript script, IEncounter encounter, ICombatService combat) BuildScript(out ICreature creature)
    {
        creature = Substitute.For<ICreature>();
        // NSubstitute auto-substitutes reference-type reads. Initialise the taunt fields so
        // the no-taunt branch in PickTarget is the default rather than picking up an
        // auto-stubbed IUnit.
        creature.TauntedBy      = null;
        creature.TauntExpiresAt = DateTime.MinValue;
        creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());

        var encounter = Substitute.For<IEncounter>();
        var combat    = Substitute.For<ICombatService>();
        combat.GetEncounterFor(creature).Returns(encounter);

        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);

        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context);
        return (script, encounter, combat);
    }

    private static (CreatureCombatScript script, ICreature creature, ICharacter target) BuildChasingScript(
        ICreatureLocomotion locomotion, Vector3 targetAt)
    {
        ICreature creature = Substitute.For<ICreature>();
        creature.Position.Returns(Vector3.zero);
        creature.TauntedBy      = null;
        creature.TauntExpiresAt = DateTime.MinValue;
        creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());

        ICharacter target = Substitute.For<ICharacter>();
        target.Position.Returns(targetAt);
        target.IsDead.Returns(false);

        // A creature that has never been given a destination has "arrived" trivially — the real
        // WaypointLocomotion returns true here too (unregistered / empty path), which is what
        // makes the very first engagement tick call MoveTo.
        locomotion.HasArrived(creature).Returns(true);

        var combat = Substitute.For<ICombatService>();
        combat.GetEncounterFor(creature).Returns((IEncounter?)null);

        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);
        context.Locomotion.Returns(locomotion);

        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context);
        // OnEnteredRange seeds State = Combat, target = character, and _initialPosition = the
        // creature's current position (Vector3.zero here) — the seam under test only cares that
        // the script is actively engaging something far enough away to need to move.
        script.OnEnteredRange(target);

        return (script, creature, target);
    }

    private static (CreatureCombatScript script, ICreature creature, ICombatService combat) BuildScriptReturningHome(
        ICreatureLocomotion locomotion, Vector3 home)
    {
        ICreature creature = Substitute.For<ICreature>();
        creature.Position.Returns(home);
        creature.TauntedBy      = null;
        creature.TauntExpiresAt = DateTime.MinValue;
        creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());
        creature.Health.Returns(100u);

        // No route home (or the last waypoint already consumed) — this is what makes the
        // Returning branch attempt a regen and then, finding nothing again, snap home.
        locomotion.HasArrived(creature).Returns(true);

        var combat = Substitute.For<ICombatService>();
        combat.GetEncounterFor(creature).Returns((IEncounter?)null);

        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);
        context.Locomotion.Returns(locomotion);

        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context);

        // Seed _initialPosition = home via OnEnteredRange while the creature is standing on it,
        // then move the creature away and have its target die — the live path that flips the
        // script into CombatState.Returning without reaching into private state.
        ICharacter target = Substitute.For<ICharacter>();
        target.IsDead.Returns(true);
        script.OnEnteredRange(target);

        creature.Position.Returns(home + new Vector3(5f, 0f, 5f));
        script.Update(TimeSpan.FromSeconds(0.1)); // target is dead -> State = Returning

        creature.ClearReceivedCalls();
        locomotion.ClearReceivedCalls();

        return (script, creature, combat);
    }
}
