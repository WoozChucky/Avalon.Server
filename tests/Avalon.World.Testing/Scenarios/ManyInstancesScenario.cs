using Avalon.Common.Mathematics;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// Many small instances: 250 private (<see cref="MapType.Normal" />) instances of the town's layout, each its own
/// <see cref="MapInstance" /> over the shared navmesh, built through the registry's private-map path
/// (<see cref="InstanceRegistry.GetOrCreateNormalInstanceAsync" />, one owner each), with two players walking the
/// 8 m loop (<see cref="TownNavmesh.LoopCentre" />) on opposite sides of it. What is measured is the fixed cost of an
/// instance's tick, many times over, with little in each.
/// </summary>
public sealed class ManyInstancesScenario : IScenario
{
    private const int InstanceCount = 250;
    private const int PlayersPerInstance = 2;

    // Not the town's id (1): a scenario world records a template's map type with its first instance.
    private const ushort TemplateId = 2;

    public string Name => "many-instances";

    public int Players => InstanceCount * PlayersPerInstance;

    public ScenarioWorld Build()
    {
        var world = new ScenarioWorld();

        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(TownNavmesh.Shared);
        if (navigator.FindGround(TownNavmesh.LoopCentre, out Vector3 centre) != NavmeshGroundKind.Under)
            throw new InvalidOperationException("The loop's centre is off the town's navmesh");

        PlayerInputHandler handler = LoopWalker.Handler(world);
        uint nextCharacter = 1;
        for (int i = 0; i < InstanceCount; i++)
        {
            MapInstance instance = world.AddInstance(MapType.Normal, TemplateId);
            for (int p = 0; p < PlayersPerInstance; p++)
            {
                Vector3 start = LoopWalker.LoopPoint(navigator, centre, p * MathF.Tau / PlayersPerInstance);
                world.Join(instance, world.NewCharacter(nextCharacter++), start, LoopWalker.Around(handler, centre));
            }
        }

        return world;
    }
}
