using System;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Scripts;

public class CreaturePatrolScriptShould
{
    [Fact]
    public void Ask_Locomotion_For_The_Next_Waypoint_Rather_Than_Moving_Itself()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        locomotion.HasArrived(Arg.Any<ICreature>()).Returns(true);
        (CreaturePatrolScript script, ICreature creature) = BuildPatrolScript(locomotion,
            Point(10f), Point(20f));

        script.Update(TimeSpan.FromSeconds(0.1));

        locomotion.Received().MoveTo(creature, new Vector3(10f, 0f, 0f));
        creature.DidNotReceive().Position = Arg.Any<Vector3>();
    }

    /// <summary>
    /// The old code only ever generated a path when it needed one. Calling MoveTo every tick
    /// would be a full path query per creature per tick at 60 Hz, so the seam must only ask
    /// locomotion again once it actually has nothing left to walk towards.
    /// </summary>
    [Fact]
    public void Not_Recalculate_The_Path_Every_Tick_While_Still_Travelling()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        // Tick 1: nothing requested yet -> "arrived" (trivially) triggers the first MoveTo.
        // Ticks 2-3: still mid-journey -> locomotion reports it has not arrived.
        locomotion.HasArrived(Arg.Any<ICreature>()).Returns(true, false, false);
        (CreaturePatrolScript script, ICreature creature) = BuildPatrolScript(locomotion, Point(10f));

        script.Update(TimeSpan.FromSeconds(0.1));
        script.Update(TimeSpan.FromSeconds(0.1));
        script.Update(TimeSpan.FromSeconds(0.1));

        locomotion.Received(1).MoveTo(creature, new Vector3(10f, 0f, 0f));
    }

    /// <summary>
    /// HasArrived reports true both for a genuinely completed leg and for a waypoint that was
    /// never reachable. Either way the route must keep progressing rather than getting stuck
    /// re-requesting the same unreachable point forever, or cycling through waypoints on the
    /// very first tick before ever asking locomotion to walk anywhere.
    /// </summary>
    [Fact]
    public void Advance_To_The_Next_Waypoint_Once_Locomotion_Has_Nothing_Left_To_Walk()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        locomotion.HasArrived(Arg.Any<ICreature>()).Returns(true);
        (CreaturePatrolScript script, ICreature creature) = BuildPatrolScript(locomotion,
            Point(10f), Point(20f));

        script.Update(TimeSpan.FromSeconds(0.1)); // requests the first waypoint
        script.Update(TimeSpan.FromSeconds(0.1)); // locomotion has nothing left -> advances
        locomotion.ClearReceivedCalls();

        script.Update(TimeSpan.FromSeconds(0.1)); // requests the second waypoint

        locomotion.Received().MoveTo(creature, new Vector3(20f, 0f, 0f));
    }

    /// <summary>
    /// A point's wait is how long the creature stands there before walking on (#421). The path
    /// definition decides it per point; 0 means walk straight on.
    /// </summary>
    [Fact]
    public void Wait_At_A_Point_For_Its_Pause_Before_Walking_On()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        locomotion.HasArrived(Arg.Any<ICreature>()).Returns(true);
        (CreaturePatrolScript script, ICreature creature) = BuildPatrolScript(locomotion,
            Point(10f, wait: TimeSpan.FromSeconds(2)), Point(20f));

        script.Update(TimeSpan.FromSeconds(0.1)); // requests the first point
        script.Update(TimeSpan.FromSeconds(0.1)); // arrives: the 2 s pause begins
        script.Update(TimeSpan.FromSeconds(1.0)); // 1.0 s into the pause
        script.Update(TimeSpan.FromSeconds(0.5)); // 1.5 s into the pause

        locomotion.DidNotReceive().MoveTo(creature, new Vector3(20f, 0f, 0f));

        script.Update(TimeSpan.FromSeconds(0.6)); // 2.1 s: the pause is over
        script.Update(TimeSpan.FromSeconds(0.1)); // requests the second point

        locomotion.Received(1).MoveTo(creature, new Vector3(20f, 0f, 0f));
    }

    /// <summary>
    /// The calling script owns the moving MoveState; locomotion sets Idle when the creature comes to
    /// rest. A pausing script that kept writing Walking would stomp that every tick, and the creature
    /// would play its walk animation standing still.
    /// </summary>
    [Fact]
    public void Not_Claim_To_Be_Walking_While_It_Pauses()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        locomotion.HasArrived(Arg.Any<ICreature>()).Returns(true);
        (CreaturePatrolScript script, ICreature creature) = BuildPatrolScript(locomotion,
            Point(10f, wait: TimeSpan.FromSeconds(5)), Point(20f));

        script.Update(TimeSpan.FromSeconds(0.1)); // requests the first point
        script.Update(TimeSpan.FromSeconds(0.1)); // arrives: the pause begins
        creature.ClearReceivedCalls();

        script.Update(TimeSpan.FromSeconds(1.0));
        script.Update(TimeSpan.FromSeconds(1.0));

        creature.DidNotReceive().MoveState = MoveState.Walking;
    }

    /// <summary>
    /// Advancing to the next point used to set State to Idle, and ShouldRun() is "State is
    /// Patrolling" — so a patrol chained under another script would have stopped for good after its
    /// first leg. MapInstance ignores ShouldRun on the top-level script, which is the only reason it
    /// never showed.
    /// </summary>
    [Fact]
    public void Keep_Patrolling_After_Each_Leg()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        locomotion.HasArrived(Arg.Any<ICreature>()).Returns(true);
        (CreaturePatrolScript script, _) = BuildPatrolScript(locomotion, Point(10f), Point(20f));

        script.Update(TimeSpan.FromSeconds(0.1));
        script.Update(TimeSpan.FromSeconds(0.1));
        script.Update(TimeSpan.FromSeconds(0.1));

        Assert.Equal(CreaturePatrolScript.PatrolState.Patrolling, script.State);
    }

    /// <summary>A creature with no path — every spawn that has none — stands where it was placed.</summary>
    [Fact]
    public void Stand_Still_When_The_Creature_Has_No_Path()
    {
        var locomotion = Substitute.For<ICreatureLocomotion>();
        locomotion.HasArrived(Arg.Any<ICreature>()).Returns(true);
        (CreaturePatrolScript script, _) = BuildPatrolScript(locomotion);

        script.Update(TimeSpan.FromSeconds(0.1));

        locomotion.DidNotReceiveWithAnyArgs().MoveTo(default!, default);
    }

    // #600: a patrolling creature fights when hit, through a chained CreatureCombatScript, then
    // resumes its path from the nearest point once the fight is over.

    [Fact]
    public void Take_Damage_When_Hit()
    {
        var fight = new PatrolFight(Point(0f), Point(10f), Point(20f));

        fight.Script.OnHit(fight.Attacker, 30);

        Assert.Equal(70u, fight.Creature.CurrentHealth);
        fight.Context.Received(1).BroadcastUnitHit(fight.Attacker, fight.Creature, 70u, 30u);
    }

    [Fact]
    public void Chase_Its_Attacker_At_A_Run_Instead_Of_Walking_The_Path()
    {
        var fight = new PatrolFight(Point(0f), Point(10f), Point(20f));
        fight.WalkToSecondPoint();
        fight.AttackerAt = new Vector3(5f, 0f, 8f);

        fight.Script.OnHit(fight.Attacker, 1);
        fight.Locomotion.ClearReceivedCalls();
        fight.Creature.ClearReceivedCalls();
        fight.Tick();

        // The patrol hands over by ending its own leg; the combat script then heads for its target.
        fight.Locomotion.Received().Stop(fight.Creature);
        fight.Locomotion.Received().MoveTo(fight.Creature,
            Arg.Is<Vector3>(v => Vector3.Distance(v, fight.AttackerAt) < 2f));
        fight.Creature.Received().MoveState = MoveState.Running;
        fight.Creature.DidNotReceive().MoveState = MoveState.Walking;
    }

    [Fact]
    public void Attack_Its_Attacker_Once_In_Range()
    {
        var fight = new PatrolFight(Point(0f), Point(10f), Point(20f));
        fight.AttackerAt = new Vector3(1f, 0f, 0f);

        fight.Script.OnHit(fight.Attacker, 1);
        fight.Tick();

        fight.Combat.Received(1).ApplyDamage(fight.Creature, fight.Attacker, Arg.Any<uint>());
    }

    /// <summary>
    /// Neither the point it was heading for nor a point's wait moves on while it fights: however long
    /// the fight, no waypoint is asked for, and the patrol stays handed over.
    /// </summary>
    [Fact]
    public void Not_Advance_The_Patrol_While_In_Combat()
    {
        var fight = new PatrolFight(Point(0f, wait: TimeSpan.FromSeconds(1)), Point(10f), Point(20f));
        fight.Tick();                         // requests the first point
        fight.Tick();                         // arrives: the 1 s pause begins
        fight.AttackerAt = new Vector3(1f, 0f, 0f);

        fight.Script.OnHit(fight.Attacker, 1);
        fight.Locomotion.ClearReceivedCalls();
        for (int i = 0; i < 20; i++)
            fight.Tick(TimeSpan.FromSeconds(1));

        foreach (PatrolPoint point in fight.Path)
            fight.Locomotion.DidNotReceive().MoveTo(fight.Creature, point.Position);
        Assert.Equal(CreaturePatrolScript.PatrolState.Idle, fight.Script.State);
    }

    /// <summary>
    /// The target died: the combat script goes home (where it was hit), and once there the patrol picks
    /// up again at the point nearest it — not at point 0, and not at the point it was walking to.
    /// </summary>
    [Fact]
    public void Resume_From_The_Nearest_Point_After_Its_Target_Dies()
    {
        var fight = new PatrolFight(Point(0f), Point(10f), Point(20f));
        fight.WalkToSecondPoint();
        fight.CreatureAt = new Vector3(18f, 0f, 0f);
        fight.AttackerAt = new Vector3(19f, 0f, 0f);

        fight.Script.OnHit(fight.Attacker, 1);
        fight.Tick();                              // fights
        fight.Attacker.IsDead.Returns(true);
        fight.CreatureAt = new Vector3(19f, 0f, 1f);
        fight.Tick();                              // target dead: heads home to (18, 0, 0)
        fight.Locomotion.Received().MoveTo(fight.Creature, new Vector3(18f, 0f, 0f));

        fight.Locomotion.ClearReceivedCalls();
        fight.CreatureAt = new Vector3(18f, 0f, 0f);
        fight.Tick();                              // home: combat is over, the patrol resumes

        fight.Locomotion.Received(1).MoveTo(fight.Creature, new Vector3(20f, 0f, 0f));
        fight.Locomotion.DidNotReceive().MoveTo(fight.Creature, new Vector3(10f, 0f, 0f));
        fight.Locomotion.DidNotReceive().MoveTo(fight.Creature, new Vector3(0f, 0f, 0f));
        Assert.Equal(CreaturePatrolScript.PatrolState.Patrolling, fight.Script.State);
        Assert.Equal(100u, fight.Creature.CurrentHealth);
    }

    /// <summary>Drawn past the leash, it goes home, then resumes from the nearest point and walks on in order.</summary>
    [Fact]
    public void Resume_From_The_Nearest_Point_After_The_Leash_And_Carry_On_In_Order()
    {
        var fight = new PatrolFight(Point(0f), Point(10f), Point(20f));
        fight.WalkToSecondPoint();
        fight.CreatureAt = new Vector3(1f, 0f, 3f);
        fight.AttackerAt = new Vector3(60f, 0f, 3f);

        fight.Script.OnHit(fight.Attacker, 1);
        fight.CreatureAt = new Vector3(50f, 0f, 3f);   // chased 49 m: past the leash
        fight.Tick();
        fight.Locomotion.Received().MoveTo(fight.Creature, new Vector3(1f, 0f, 3f));

        fight.Locomotion.ClearReceivedCalls();
        fight.CreatureAt = new Vector3(1f, 0f, 3f);
        fight.Tick();                                  // home: resumes at point 0, the nearest
        fight.Locomotion.Received(1).MoveTo(fight.Creature, new Vector3(0f, 0f, 0f));

        fight.Tick();                                  // leg done: advances
        fight.Tick();                                  // and asks for the next point in order
        fight.Locomotion.Received(1).MoveTo(fight.Creature, new Vector3(10f, 0f, 0f));
    }

    /// <summary>OnCharacterLeft reaches the chained combat script: its target left, so it goes home at full health.</summary>
    [Fact]
    public void Tell_The_Chained_Combat_Script_When_A_Character_Leaves()
    {
        var fight = new PatrolFight(Point(0f), Point(10f));
        fight.CreatureAt = new Vector3(4f, 0f, 0f);
        fight.AttackerAt = new Vector3(5f, 0f, 0f);
        fight.Script.OnHit(fight.Attacker, 30);
        fight.Tick();
        fight.Locomotion.ClearReceivedCalls();

        fight.Script.OnCharacterLeft(fight.Attacker);

        fight.Locomotion.Received(1).MoveTo(fight.Creature, new Vector3(4f, 0f, 0f));
        Assert.Equal(100u, fight.Creature.CurrentHealth);
    }

    /// <summary>A corpse's patrol stops for good: no leg, no pause, no MoveState, however long it is ticked.</summary>
    [Fact]
    public void Stop_For_Good_Once_Dead()
    {
        var fight = new PatrolFight(Point(0f), Point(10f));
        fight.Tick();                          // requests the first point

        fight.Script.OnHit(fight.Attacker, 100);
        fight.Locomotion.ClearReceivedCalls();
        fight.Creature.ClearReceivedCalls();
        for (int i = 0; i < 5; i++)
            fight.Tick(TimeSpan.FromSeconds(1));

        Assert.Equal(0u, fight.Creature.CurrentHealth);
        fight.Locomotion.DidNotReceiveWithAnyArgs().MoveTo(default!, default);
        fight.Creature.DidNotReceiveWithAnyArgs().MoveState = default;
    }

    /// <summary>
    /// A creature driven by a patrol script, with a combat service, a character attacker and a position
    /// the test moves by hand, the way the locomotion would.
    /// </summary>
    private sealed class PatrolFight
    {
        public PatrolFight(params PatrolPoint[] path)
        {
            Path = path;
            Locomotion.HasArrived(Arg.Any<ICreature>()).Returns(true);

            Creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 600));
            Creature.Position.Returns(_ => CreatureAt);
            var metadata = Substitute.For<ICreatureMetadata>();
            metadata.SpeedRun.Returns(4f);
            metadata.SpeedWalk.Returns(2f);
            Creature.Metadata.Returns(metadata);
            Creature.PatrolPath.Returns(path);
            Creature.Health.Returns(100u);
            Creature.CurrentHealth = 100;
            Creature.DamageMin.Returns(1u);
            Creature.DamageMax.Returns(1u);
            Creature.TauntedBy = null;

            Attacker.Guid.Returns(new ObjectGuid(ObjectType.Character, 601));
            Attacker.Position.Returns(_ => AttackerAt);
            Attacker.IsDead.Returns(false);

            Combat.GetEncounterFor(Creature).Returns((IEncounter?)null);
            Context.Locomotion.Returns(Locomotion);
            Context.CombatService.Returns(Combat);

            Script = new CreaturePatrolScript(NullLoggerFactory.Instance, Creature, Context);
        }

        public PatrolPoint[] Path { get; }
        public ICreatureLocomotion Locomotion { get; } = Substitute.For<ICreatureLocomotion>();
        public ICreature Creature { get; } = Substitute.For<ICreature>();
        public ICharacter Attacker { get; } = Substitute.For<ICharacter>();
        public ICombatService Combat { get; } = Substitute.For<ICombatService>();
        public ISimulationContext Context { get; } = Substitute.For<ISimulationContext>();
        public CreaturePatrolScript Script { get; }
        public Vector3 CreatureAt { get; set; }
        public Vector3 AttackerAt { get; set; } = new(100f, 0f, 100f);

        public void Tick(TimeSpan? deltaTime = null) => Script.Update(deltaTime ?? TimeSpan.FromSeconds(0.1));

        /// <summary>Requests point 0, finishes that leg, and requests point 1.</summary>
        public void WalkToSecondPoint()
        {
            Tick();
            Tick();
            Tick();
            Locomotion.Received(1).MoveTo(Creature, Path[1].Position);
        }
    }

    private static PatrolPoint Point(float x, TimeSpan wait = default) => new(new Vector3(x, 0f, 0f), wait);

    private static (CreaturePatrolScript script, ICreature creature) BuildPatrolScript(
        ICreatureLocomotion locomotion, params PatrolPoint[] path)
    {
        ICreature creature = Substitute.For<ICreature>();
        creature.Position.Returns(Vector3.zero);
        creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());
        creature.PatrolPath.Returns(path);

        var context = Substitute.For<ISimulationContext>();
        context.Locomotion.Returns(locomotion);

        var script = new CreaturePatrolScript(NullLoggerFactory.Instance, creature, context);
        return (script, creature);
    }
}
