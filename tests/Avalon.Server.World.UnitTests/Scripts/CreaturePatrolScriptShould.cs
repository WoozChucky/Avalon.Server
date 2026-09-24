using System;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Creatures;
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
        (CreaturePatrolScript script, ICreature creature) = BuildPatrolScript(locomotion, waypoints:
            [new Vector3(10f, 0f, 0f), new Vector3(20f, 0f, 0f)]);

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
        (CreaturePatrolScript script, ICreature creature) = BuildPatrolScript(locomotion, waypoints:
            [new Vector3(10f, 0f, 0f)]);

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
        (CreaturePatrolScript script, ICreature creature) = BuildPatrolScript(locomotion, waypoints:
            [new Vector3(10f, 0f, 0f), new Vector3(20f, 0f, 0f)]);

        script.Update(TimeSpan.FromSeconds(0.1)); // requests the first waypoint
        script.Update(TimeSpan.FromSeconds(0.1)); // locomotion has nothing left -> advances
        locomotion.ClearReceivedCalls();

        script.Update(TimeSpan.FromSeconds(0.1)); // requests the second waypoint

        locomotion.Received().MoveTo(creature, new Vector3(20f, 0f, 0f));
    }

    private static (CreaturePatrolScript script, ICreature creature) BuildPatrolScript(
        ICreatureLocomotion locomotion, Vector3[] waypoints)
    {
        ICreature creature = Substitute.For<ICreature>();
        creature.Position.Returns(Vector3.zero);
        creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());

        var context = Substitute.For<ISimulationContext>();
        context.Locomotion.Returns(locomotion);

        var script = new CreaturePatrolScript(NullLoggerFactory.Instance, creature, context, waypoints);
        return (script, creature);
    }
}
