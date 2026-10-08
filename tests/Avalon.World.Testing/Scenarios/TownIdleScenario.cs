using Avalon.Common.Mathematics;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// The quiet floor: one town, 30 players standing still on a grid around its entry spawn, no creatures and no input.
/// Every player sees every other, so each is sent the others once and then only what changes, which is nothing: what
/// is left is the cost of a busy town's tick with nobody doing anything.
/// </summary>
public sealed class TownIdleScenario : IScenario
{
    private const int Columns = 6;
    private const float Spacing = 1.5f;

    public string Name => "town-idle";

    public int Players => 30;

    public ScenarioWorld Build()
    {
        var world = new ScenarioWorld();
        MapInstance town = world.AddInstance(MapType.Town);

        // Each grid point stands on the ground of the navmesh the instance walks on.
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(TownNavmesh.Shared);

        Vector3 spawn = TownNavmesh.EntrySpawn;
        int rows = (Players + Columns - 1) / Columns;
        for (int p = 0; p < Players; p++)
        {
            float x = spawn.x + (p % Columns - (Columns - 1) / 2f) * Spacing;
            float z = spawn.z + (p / Columns - (rows - 1) / 2f) * Spacing;
            if (navigator.FindGround(new Vector3(x, spawn.y, z), out Vector3 ground) != NavmeshGroundKind.Under)
                throw new InvalidOperationException($"Grid point ({x}, {z}) is off the town's navmesh");

            world.Join(town, world.NewCharacter((uint)(1 + p)), ground);
        }

        return world;
    }
}
