using System;
using Avalon.Common.Mathematics;
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
