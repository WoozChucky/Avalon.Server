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
    /// Round 3: attacking no longer calls Stop merely because the creature is in range — Stop is
    /// reserved for actually disengaging (leash, target switch, target lost). This is the
    /// deliberate reversal of what an earlier round's
    /// <c>Stop_The_Real_Locomotion_When_Entering_Attack_Range</c> test pinned: a creature that is
    /// within AttackRange of the target but still short of its own destination now keeps
    /// stepping toward it AND swings in the same tick, rather than freezing the instant it
    /// crosses the AttackRange boundary — freezing there is what caused four chasers to collapse
    /// onto a couple of ring-crossing points once the target itself was moving. A mocked
    /// <see cref="ICreatureLocomotion"/> can't prove the path was actually left running, so this
    /// uses a real one, same fixture shape as the test it replaces.
    /// </summary>
    [Fact]
    public void Keep_Stepping_Toward_Its_Destination_While_Also_Attacking_In_Range()
    {
        var navigator = Substitute.For<IMapNavigator>();
        var locomotion = new WaypointLocomotion(_ => navigator);

        ICreature creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 1));
        creature.TauntedBy      = null;
        creature.TauntExpiresAt = DateTime.MinValue;

        var metadata = Substitute.For<ICreatureMetadata>();
        metadata.SpeedRun.Returns(4f);
        creature.Metadata.Returns(metadata);
        creature.Speed.Returns(4f);
        creature.Position.Returns(Vector3.zero);

        var targetPosition = new Vector3(2f, 0f, 0f);
        ICharacter target = Substitute.For<ICharacter>();
        target.Guid.Returns(new ObjectGuid(ObjectType.Character, 2));
        target.Position.Returns(targetPosition);
        target.IsDead.Returns(false);

        var combat = Substitute.For<ICombatService>();
        combat.GetEncounterFor(creature).Returns((IEncounter?)null);

        // MeleeSlots left unconfigured (auto-recursive substitute -> TryClaim returns false) so
        // this is unambiguously the plain "is it in range of the target" case: the destination
        // the creature is still walking toward below is the target's own centre, not a ring slot.
        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);
        context.Locomotion.Returns(locomotion);

        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, context);
        script.OnEnteredRange(target); // _initialPosition = Vector3.zero, State = Combat

        // A path toward the target's centre is already loaded and not yet consumed.
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns([targetPosition]);
        locomotion.Register(creature, radius: 0.5f, maxSpeed: 4f);
        locomotion.MoveTo(creature, targetPosition);

        // Within AttackRange (1.5f) of the target, but not yet standing on the waypoint
        // (WaypointLocomotion's 0.1f arrival threshold).
        creature.Position.Returns(new Vector3(1f, 0f, 0f));

        script.Update(TimeSpan.FromSeconds(0.1));      // AI scripts tick first...

        combat.Received(1).ApplyDamage(creature, target, 10u);

        locomotion.Update(TimeSpan.FromSeconds(0.1));  // ...then locomotion, same as MapInstance.Update.

        // Position DID change — the creature kept stepping toward the target this tick. Attacking
        // did not freeze its footing.
        creature.Received().Position = Arg.Is<Vector3>(p => Vector3.Distance(p, new Vector3(1f, 0f, 0f)) > 0.3f);
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
    /// not relying on Cos/Sin rounding luck): without AttackRangeArrivalMargin (and
    /// Context.Locomotion.ArrivalTolerance), the script would never attack here at all — it
    /// would settle at its slot (locomotion reports arrival, and the drift check finds nothing
    /// to correct, so it never re-paths to close the residual gap) yet sit a hair outside plain
    /// AttackRange, forever. A mocked ICreatureLocomotion can't catch this — ArrivalTolerance
    /// would just return whatever it's told, regardless of whether the real WaypointLocomotion
    /// actually settles a creature within its own advertised tolerance — so this uses a real one.
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
    /// Round 3: the AttackRangeArrivalMargin tolerance is unconditional now — no longer gated on
    /// hasSlot or on Locomotion.HasArrived (earlier rounds gated it on both, to avoid an
    /// oscillation that no longer exists now that attacking doesn't call Stop). A surplus
    /// creature (ring full, no slot of its own) still gets the same tolerance a slotted creature
    /// would, because the reason for it — locomotion's own arrival imprecision — applies just as
    /// much to a creature settling near the target's exact centre as to one settling near a ring
    /// slot. A mutation that re-adds gating on hasSlot would stop attacking here.
    /// </summary>
    [Fact]
    public void Attack_A_Surplus_Creature_Within_The_Tolerance_Band_Too()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        var targetPosition = Vector3.zero;
        (CreatureCombatScript script, ICreature creature, ICharacter target) =
            BuildChasingScript(locomotion, targetAt: targetPosition, slotCount: 1);

        // Another creature already holds the only slot — this one is a surplus attacker with no
        // slot of its own.
        ClaimEverySlot(target);

        // 1.6 is past the plain AttackRange (1.5) but within 1.5 + 0.2 ArrivalTolerance + 0.05
        // margin = 1.75.
        creature.Position.Returns(new Vector3(1.6f, 0f, 0f));
        locomotion.ArrivalTolerance(creature).Returns(0.2f);

        script.Update(TimeSpan.FromSeconds(0.1));

        creature.Received(1).LookAt(targetPosition);
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

        // The server's real tick rate (~16.67ms), not a coarser one: at 0.1s the per-tick step
        // (~0.4 units at SpeedRun 4) jumps clean over WaypointLocomotion's 0.1f arrival window, so
        // creatures never register as arrived and this test's separation would pass on oscillation
        // phase alone rather than on creatures actually standing in their slots.
        TimeSpan tickInterval = TimeSpan.FromSeconds(1.0 / 60.0);

        // Enough ticks to cross ~8.5 units at 4 units/sec (~129 ticks at 1/60s) plus headroom for
        // at least one full AttackCooldown (2.25s) after arrival, so "dealt damage" is a
        // meaningful assertion and not just "happened to get one swing in right as it arrived".
        for (int tick = 0; tick < 400; tick++)
        {
            foreach (CreatureCombatScript script in scripts)
            {
                script.Update(tickInterval);
            }

            locomotion.Update(tickInterval);
        }

        // Separation alone doesn't prove the claim — a creature that never arrives (and so never
        // settles into a fixed, non-overlapping position) can still happen to be far enough from
        // the others at the moment the test samples it. Assert arrival and damage-dealt for each
        // of the four explicitly: the claim is that four creatures surround a target and fight
        // it, not merely that a snapshot of their positions happens to be spread out.
        for (int i = 0; i < creatures.Count; i++)
        {
            Assert.True(locomotion.HasArrived(creatures[i]), $"Creature {i} never arrived at its slot.");
            combat.Received().ApplyDamage(creatures[i], target, 10u);
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

    /// <summary>
    /// The case that actually matters: players move essentially all the time. Before this round,
    /// in-range was measured to the target's centre and reaching it called Stop, which discarded
    /// the destination and froze the creature wherever it happened to be — there was no re-path
    /// as the slot moved, so a slotted chaser's footing was only ever correct for the instant it
    /// first arrived. A steadily translating target (1 u/s — slower than every chaser's 4 u/s
    /// SpeedRun, so catching up is possible at all) drags its slots along with it; this asserts
    /// the four chasers keep pace, stay pairwise separated, and keep attacking throughout, not
    /// just at one sampled instant.
    /// </summary>
    [Fact]
    public void Keep_Several_Chasers_Of_A_Moving_Target_Separated_And_Attacking()
    {
        var navigator = Substitute.For<IMapNavigator>();
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns(call => new List<Vector3> { call.ArgAt<Vector3>(1) });

        var locomotion = new WaypointLocomotion(_ => navigator);
        var meleeSlots = new MeleeSlots(slotCount: 6, radius: 1.5f);

        var combat = Substitute.For<ICombatService>();
        combat.GetEncounterFor(Arg.Any<IUnit>()).Returns((IEncounter?)null);

        var targetVelocity = new Vector3(1f, 0f, 0f); // 1 u/s — slower than every chaser.
        ICharacter target = Substitute.For<ICharacter>();
        target.Guid.Returns(new ObjectGuid(ObjectType.Character, 100));
        target.Position.Returns(Vector3.zero);
        target.IsDead.Returns(false);

        var context = Substitute.For<ISimulationContext>();
        context.CombatService.Returns(combat);
        context.Locomotion.Returns(locomotion);
        context.MeleeSlots.Returns(meleeSlots);

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

        TimeSpan tickInterval = TimeSpan.FromSeconds(1.0 / 60.0);
        const float agentDiameter = 1.2f;

        // 10 simulated seconds: long enough to close the initial ~8.5-unit gap against a target
        // that keeps receding at 1 u/s, then sustain formation for several AttackCooldown (2.25s)
        // cycles afterward — this is checking the whole journey, not just where it ends up.
        float elapsed = 0f;
        int ticks = (int)(10.0 / tickInterval.TotalSeconds);
        for (int tick = 0; tick < ticks; tick++)
        {
            elapsed += (float)tickInterval.TotalSeconds;
            target.Position.Returns(targetVelocity * elapsed);

            foreach (CreatureCombatScript script in scripts)
            {
                script.Update(tickInterval);
            }

            locomotion.Update(tickInterval);

            // Sampled throughout, not just at the end: a moving target's slots are never
            // permanently "arrived at", so the only assertion that means anything here is that
            // separation holds continuously once it's first established, not at one instant.
            if (tick < ticks / 2)
            {
                continue;
            }

            for (int i = 0; i < creatures.Count; i++)
            {
                for (int j = i + 1; j < creatures.Count; j++)
                {
                    float distance = Vector3.Distance(creatures[i].Position, creatures[j].Position);
                    Assert.True(distance >= agentDiameter,
                        $"Tick {tick}: creatures {i} and {j} were only {distance} apart — closer than one agent diameter ({agentDiameter}).");
                }
            }
        }

        for (int i = 0; i < creatures.Count; i++)
        {
            combat.Received().ApplyDamage(creatures[i], target, 10u);
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
