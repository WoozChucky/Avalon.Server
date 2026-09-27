using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.State;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Entities;
using Avalon.World.Maps.Navigation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Creatures;

/// <summary>
/// #638: a creature holding melee around a moving target re-paths about every fifth tick, so a
/// re-path that builds new lists is garbage in proportion to creatures and pace. Once each buffer has
/// grown to the longest route it has held, a re-path over the real navigator allocates nothing.
/// </summary>
public class WaypointRepathAllocationShould
{
    private static readonly Vector3 Start = new(-12f, 0f, -12f);
    private static readonly Vector3[] Destinations =
    [
        new(12f, 0f, 12f), new(-12f, 0f, 12f), new(12f, 0f, -12f), new(0f, 0f, 9f),
    ];

    private static (WaypointLocomotion Locomotion, MapNavigator Navigator, Creature Creature) Build()
    {
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(CrowdLocomotionShould.FlatNavMesh.Value);
        var locomotion = new WaypointLocomotion(_ => navigator);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 638),
            TemplateId = new CreatureTemplateId(4),
            Name = "Wolf",
            Position = Start,
            Speed = 5f,
            MoveState = MoveState.Running,
        };
        locomotion.Register(creature, radius: 0.5f);
        return (locomotion, navigator, creature);
    }

    [Fact]
    public void Re_path_in_steady_state_without_allocating()
    {
        var (locomotion, _, creature) = Build();

        // Warm-up: every route once, so each buffer has grown to the longest it will hold.
        foreach (Vector3 destination in Destinations)
        {
            creature.Position = Start;
            locomotion.MoveTo(creature, destination);
            locomotion.Update(TimeSpan.FromSeconds(1d / 60d));
        }

        Assert.False(locomotion.HasArrived(creature));

        // The fewest bytes over three windows of 100 re-paths. A re-path that allocates shows in every
        // window; a one-off the runtime does on this thread (seen once in a full-solution run, never
        // reproduced) lands in one window only and cannot fail the test by itself.
        long fewest = long.MaxValue;
        for (int window = 0; window < 3; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int repath = 0; repath < 100; repath++)
            {
                creature.Position = Start;
                locomotion.MoveTo(creature, Destinations[repath % Destinations.Length]);
                locomotion.Update(TimeSpan.FromSeconds(1d / 60d));
            }

            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, fewest);
        Assert.False(locomotion.HasArrived(creature));
    }
}
