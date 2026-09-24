using System;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Creatures;
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
    // Set by BuildChasingScript, read back by SlotsOf/ClaimEverySlot — the concrete MeleeSlots
    // backing whichever ICreatureLocomotion-flavoured context the current test built.
    private MeleeSlots? _meleeSlots;

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

    [Fact]
    public void Chase_Its_Own_Slot_Rather_Than_The_Targets_Centre()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        var targetPosition = new Vector3(20f, 0f, 0f);
        (CreatureCombatScript script, ICreature creature, _) =
            BuildChasingScript(locomotion, targetAt: targetPosition);

        script.Update(TimeSpan.FromSeconds(0.1));

        locomotion.Received().MoveTo(creature, Arg.Is<Vector3>(d => Vector3.Distance(d, targetPosition) > 1.4f));
    }

    /// <summary>Review Focus 1, at the script level: the surplus still chases.</summary>
    [Fact]
    public void Fall_Back_To_The_Targets_Centre_When_No_Slot_Is_Free()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        var targetPosition = new Vector3(20f, 0f, 0f);
        (CreatureCombatScript script, ICreature creature, ICharacter target) =
            BuildChasingScript(locomotion, targetAt: targetPosition, slotCount: 1);

        // Another creature already holds the only slot.
        ClaimEverySlot(target);

        script.Update(TimeSpan.FromSeconds(0.1));

        locomotion.Received().MoveTo(creature, targetPosition);
    }

    [Fact]
    public void Release_Its_Slot_When_It_Stops_Chasing()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        (CreatureCombatScript script, ICreature creature, ICharacter target) =
            BuildChasingScript(locomotion, targetAt: new Vector3(20f, 0f, 0f), slotCount: 1);

        script.Update(TimeSpan.FromSeconds(0.1));
        KillTarget(target, script);

        // The slot it held is free for someone else.
        Assert.True(SlotsOf(script).TryClaim(target.Guid, new ObjectGuid(ObjectType.Creature, 99),
            target.Position, target.Position, out _));
    }

    /// <summary>
    /// Pins the AttackRange boundary hazard: MeleeSlotRadius defaults to the same value as
    /// AttackRange, so a creature resting in its claimed slot sits exactly on that boundary —
    /// and WaypointLocomotion's own 0.1f arrival epsilon means it stops NEAR the slot, not on
    /// it, so raw distance-to-target-centre can land on either side. This reproduces the
    /// "stuck past the boundary" shape of that bug deterministically (distance fixed at 1.59,
    /// not relying on Cos/Sin rounding luck): unfixed, the script never attacks at all here —
    /// it re-issues MoveTo back toward the target's centre every tick, forever, undoing Stop.
    /// A mocked ICreatureLocomotion can't catch this either — HasArrived would just return
    /// whatever it's told, regardless of whether the fix's tolerance is wired up correctly — so
    /// this uses a real WaypointLocomotion, matching Stop_The_Real_Locomotion_When_Entering_Attack_Range.
    /// </summary>
    [Fact]
    public void Keep_Attacking_Once_Arrived_At_Its_Claimed_Slot_Rather_Than_Alternating()
    {
        var navigator = Substitute.For<IMapNavigator>();
        var locomotion = new WaypointLocomotion(_ => navigator);
        var meleeSlots = new MeleeSlots(slotCount: 6, radius: 1.5f);

        ICreature creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 1));
        creature.TauntedBy      = null;
        creature.TauntExpiresAt = DateTime.MinValue;
        creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());
        creature.Speed.Returns(4f);

        var targetPosition = Vector3.zero;
        ICharacter target = Substitute.For<ICharacter>();
        target.Guid.Returns(new ObjectGuid(ObjectType.Character, 2));
        target.Position.Returns(targetPosition);
        target.IsDead.Returns(false);

        var combat = Substitute.For<ICombatService>();
        combat.GetEncounterFor(creature).Returns((IEncounter?)null);

        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);
        context.Locomotion.Returns(locomotion);
        context.MeleeSlots.Returns(meleeSlots);

        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context);

        creature.Position.Returns(new Vector3(10f, 0f, 0f));
        script.OnEnteredRange(target); // _initialPosition = (10, 0, 0), State = Combat

        // Slot 0 sits at (1.5, 0, 0) — exactly AttackRange from the target's centre by
        // construction (MeleeSlotRadius == AttackRange). Load a path whose only waypoint is
        // that slot, then place the creature 0.09 short of it — inside WaypointLocomotion's
        // 0.1f arrival threshold, so the very next locomotion.Update consumes the waypoint —
        // while its raw distance to the target's centre (1.59) sits just past AttackRange.
        var slotPosition = new Vector3(1.5f, 0f, 0f);
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns([slotPosition]);
        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        creature.Position.Returns(new Vector3(1.59f, 0f, 0f));
        locomotion.MoveTo(creature, slotPosition);
        locomotion.Update(TimeSpan.FromSeconds(0.1)); // consumes the waypoint -> HasArrived == true

        script.Update(TimeSpan.FromSeconds(0.1));

        combat.Received(1).ApplyDamage(creature, target, 10u);
        creature.Received(1).LookAt(targetPosition);

        // Next tick: still resting at the same spot, still 0.09 past AttackRange by raw
        // distance. Unfixed, this re-evaluates false every time and re-issues MoveTo back to
        // the target's centre, undoing Stop — the exact oscillation this test pins.
        locomotion.Update(TimeSpan.FromSeconds(0.1));
        script.Update(TimeSpan.FromSeconds(0.1));

        creature.Received(2).LookAt(targetPosition);
        creature.DidNotReceive().Position = Arg.Any<Vector3>();
    }

    /// <summary>
    /// Pins the "no slot" half of the in-range gating: a surplus creature (ring full) has no
    /// slot of its own to have arrived at, so it must never get the arrival tolerance — only the
    /// plain AttackRange test. A mutation that applies Context.Locomotion.ArrivalTolerance
    /// unconditionally (ignoring whether this creature holds a slot at all) would stop and attack
    /// here instead — the mocked locomotion is stubbed with a non-zero ArrivalTolerance so that
    /// mutation is actually observable, rather than accidentally masked by the mock's default
    /// (unstubbed members return 0f, which alone would not have covered 1.6).
    /// </summary>
    [Fact]
    public void Not_Attack_A_Surplus_Creature_Merely_Because_It_Is_Within_The_Arrival_Tolerance()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        var targetPosition = Vector3.zero;
        (CreatureCombatScript script, ICreature creature, ICharacter target) =
            BuildChasingScript(locomotion, targetAt: targetPosition, slotCount: 1);

        // Another creature already holds the only slot — this one is a surplus attacker.
        ClaimEverySlot(target);

        // 1.6 is past the plain AttackRange (1.5) a slotless creature is held to, but would be
        // within a slotted creature's tolerance band (1.5 + 0.2 ArrivalTolerance + 0.05 margin =
        // 1.75) if the gating on hasSlot were ever bypassed — stubbing a non-zero
        // ArrivalTolerance here (rather than leaving the mock's default 0f) is what makes that
        // mutation actually observable.
        creature.Position.Returns(new Vector3(1.6f, 0f, 0f));
        locomotion.HasArrived(creature).Returns(true);
        locomotion.ArrivalTolerance(creature).Returns(0.2f);

        script.Update(TimeSpan.FromSeconds(0.1));

        locomotion.DidNotReceive().Stop(creature);
        creature.DidNotReceive().LookAt(Arg.Any<Vector3>());
    }

    /// <summary>
    /// Pins the "not yet arrived" half of the in-range gating: a creature that holds a slot but
    /// is still mid-walk to it must not get the arrival tolerance either. A mutation that applies
    /// it whenever a slot is held, without also requiring <c>Locomotion.HasArrived</c>, would
    /// stop and attack here — mid-transit, short of its own slot — instead of continuing.
    /// </summary>
    [Fact]
    public void Not_Attack_A_Slotted_Creature_Within_The_Tolerance_Band_Before_It_Arrives()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        var targetPosition = Vector3.zero;
        (CreatureCombatScript script, ICreature creature, _) =
            BuildChasingScript(locomotion, targetAt: targetPosition);

        // Default slotCount (6) — the ring is not full, so this creature holds a slot — but it
        // has not arrived: still mid-transit toward it. Stubbing a non-zero ArrivalTolerance
        // (rather than the mock's default 0f) is what would make a mutation dropping the
        // HasArrived guard actually observable: 1.6 falls inside 1.5 + 0.2 + 0.05 = 1.75.
        creature.Position.Returns(new Vector3(1.6f, 0f, 0f));
        locomotion.HasArrived(creature).Returns(false);
        locomotion.ArrivalTolerance(creature).Returns(0.2f);

        script.Update(TimeSpan.FromSeconds(0.1));

        locomotion.DidNotReceive().Stop(creature);
        creature.DidNotReceive().LookAt(Arg.Any<Vector3>());
    }

    /// <summary>
    /// The bug this whole feature exists to fix, reproduced end to end: several creatures
    /// converging on one target from roughly the same direction must end up standing apart, not
    /// stacked. Every test above uses a mocked <see cref="ICreatureLocomotion"/>, which can
    /// assert what <c>MoveTo</c> was called with but never actually advances a position — so
    /// none of them would have caught the defect this scenario is built from: the in-range check
    /// used to fire the instant a creature crossed the AttackRange circle on its approach,
    /// regardless of whether that crossing point was anywhere near its own claimed slot. Four
    /// creatures approaching from nearly the same bearing used to collapse onto about two
    /// positions. This drives four creatures — sharing one <see cref="MeleeSlots"/> and one real
    /// <see cref="WaypointLocomotion"/>, exactly as <c>MapInstance</c> does — through enough
    /// ticks to actually arrive, then checks the one thing that matters: their rest positions
    /// don't overlap.
    /// </summary>
    [Fact]
    public void Keep_Several_Chasers_Of_One_Target_At_Least_One_Agent_Diameter_Apart()
    {
        var navigator = Substitute.For<IMapNavigator>();
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns(call => new List<Vector3> { call.ArgAt<Vector3>(1) });

        var locomotion = new WaypointLocomotion(_ => navigator);
        var meleeSlots = new MeleeSlots(slotCount: 6, radius: 1.5f);

        var combat = Substitute.For<ICombatService>();
        combat.GetEncounterFor(Arg.Any<IUnit>()).Returns((IEncounter?)null);

        ICharacter target = Substitute.For<ICharacter>();
        target.Guid.Returns(new ObjectGuid(ObjectType.Character, 100));
        target.Position.Returns(Vector3.zero);
        target.IsDead.Returns(false);

        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);
        context.Locomotion.Returns(locomotion);
        context.MeleeSlots.Returns(meleeSlots);

        // Four creatures approaching from roughly the same direction (+X, slightly fanned in Z)
        // — the exact shape the review described: four creatures arriving from one direction
        // collapsing to about two positions.
        Vector3[] starts =
        {
            new(10f, 0f, 0f),
            new(10f, 0f, 0.3f),
            new(10f, 0f, -0.3f),
            new(10f, 0f, 0.6f),
        };

        var creatures = new List<ICreature>();
        var scripts = new List<CreatureCombatScript>();

        for (int i = 0; i < starts.Length; i++)
        {
            ICreature creature = Substitute.For<ICreature>();
            creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, (uint)(i + 1)));
            creature.Position.Returns(starts[i]);
            creature.TauntedBy      = null;
            creature.TauntExpiresAt = DateTime.MinValue;

            var metadata = Substitute.For<ICreatureMetadata>();
            metadata.SpeedRun.Returns(4f);
            creature.Metadata.Returns(metadata);

            locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);

            var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context);
            script.OnEnteredRange(target);

            creatures.Add(creature);
            scripts.Add(script);
        }

        // Enough ticks to cross ~8.5 units at 4 units/sec (~21 ticks), with generous headroom.
        for (int tick = 0; tick < 100; tick++)
        {
            foreach (CreatureCombatScript script in scripts)
            {
                script.Update(TimeSpan.FromSeconds(0.1));
            }

            locomotion.Update(TimeSpan.FromSeconds(0.1));
        }

        const float agentDiameter = 1.2f;
        for (int i = 0; i < creatures.Count; i++)
        {
            for (int j = i + 1; j < creatures.Count; j++)
            {
                float distance = Vector3.Distance(creatures[i].Position, creatures[j].Position);
                Assert.True(distance >= agentDiameter,
                    $"Creatures {i} and {j} ended up only {distance} apart — closer than one agent diameter ({agentDiameter}).");
            }
        }
    }

    /// <summary>Claims every slot on <paramref name="target"/> on behalf of other creatures.</summary>
    private void ClaimEverySlot(ICharacter target)
    {
        MeleeSlots slots = _meleeSlots ?? throw new InvalidOperationException("Call BuildChasingScript first.");

        uint claimantId = 900;
        while (slots.TryClaim(target.Guid, new ObjectGuid(ObjectType.Creature, claimantId), target.Position, target.Position, out _))
        {
            claimantId++;
        }
    }

    /// <summary>Marks the target dead and runs the tick that makes the script react to it.</summary>
    private static void KillTarget(ICharacter target, CreatureCombatScript script)
    {
        target.IsDead.Returns(true);
        script.Update(TimeSpan.FromSeconds(0.1));
    }

    /// <summary>The MeleeSlots backing <paramref name="script"/>'s context, built by BuildChasingScript.</summary>
    private MeleeSlots SlotsOf(CreatureCombatScript script) =>
        _meleeSlots ?? throw new InvalidOperationException("Call BuildChasingScript first.");

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

    private (CreatureCombatScript script, ICreature creature, ICharacter target) BuildChasingScript(
        ICreatureLocomotion locomotion, Vector3 targetAt, int slotCount = 6)
    {
        ICreature creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 1));
        creature.Position.Returns(Vector3.zero);
        creature.TauntedBy      = null;
        creature.TauntExpiresAt = DateTime.MinValue;
        creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());

        ICharacter target = Substitute.For<ICharacter>();
        target.Guid.Returns(new ObjectGuid(ObjectType.Character, 1));
        target.Position.Returns(targetAt);
        target.IsDead.Returns(false);

        // A creature that has never been given a destination has "arrived" trivially — the real
        // WaypointLocomotion returns true here too (unregistered / empty path), which is what
        // makes the very first engagement tick call MoveTo.
        locomotion.HasArrived(creature).Returns(true);

        var combat = Substitute.For<ICombatService>();
        combat.GetEncounterFor(creature).Returns((IEncounter?)null);

        // Radius 1.5f matches AttackRange (GameConfiguration's default MeleeSlotRadius), so a
        // creature that has arrived at its slot is already within attack range.
        _meleeSlots = new MeleeSlots(slotCount, radius: 1.5f);

        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);
        context.Locomotion.Returns(locomotion);
        context.MeleeSlots.Returns(_meleeSlots);

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
