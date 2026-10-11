using Avalon.Common.Mathematics;
using Avalon.World.Maps.Navigation;
using Avalon.World.Testing.Navigation;
using DotRecast.Detour;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Maps.Navigation;

/// <summary>
/// Characterizes the production bake's dependency on Recast results: dropping intermediate heightfields/contours must
/// preserve tile payloads and the movement queries the server consumes. Empty meshes and changed obstacle boundaries
/// must fail these checks; comparing only the setting itself would not protect either behavior.
/// </summary>
public class NavmeshBakeShould
{
    private static readonly Lazy<Bakes> s_bakes = new(Build);

    [Fact]
    public void Preserve_serialized_navigation_data_when_intermediate_results_are_released()
    {
        Bakes bakes = s_bakes.Value;
        byte[][] retained = NavmeshBakeFixture.CanonicalTileData(bakes.Retained);
        byte[][] released = NavmeshBakeFixture.CanonicalTileData(bakes.Released);
        byte[][] production = NavmeshBakeFixture.CanonicalTileData(bakes.Production);
        Assert.True(retained.Length > 4, "The town fixture must exercise several tiles across chunk boundaries");
        Assert.Equal(retained.Length, released.Length);
        Assert.Equal(retained.Length, production.Length);
        for (int i = 0; i < retained.Length; i++)
        {
            Assert.Equal(retained[i], released[i]);
            Assert.Equal(retained[i], production[i]);
        }
    }

    [Fact]
    public void Preserve_routes_walkable_rays_and_ground_queries_when_intermediate_results_are_released()
    {
        Bakes bakes = s_bakes.Value;
        MapNavigator retained = Navigator(bakes.Retained);
        MapNavigator released = Navigator(bakes.Released);
        MapNavigator production = Navigator(bakes.Production);
        var start = new Vector3(45f, 0f, 10f);
        var end = new Vector3(52f, 0f, 10f);
        List<Vector3> route = retained.FindPath(start, end);
        Assert.True(route.Count > 10, "An actual route through the open south-east town square is required");
        Assert.InRange(MathF.Abs(route[^1].x - end.x), 0f, 0.05f);
        Assert.InRange(MathF.Abs(route[^1].z - end.z), 0f, 0.05f);
        Assert.Equal(route, released.FindPath(start, end));
        Assert.Equal(route, production.FindPath(start, end));

        var outside = new Vector3(100f, 0f, 10f);
        Vector3 stopped = retained.RaycastWalkable(start, outside);
        Assert.InRange(stopped.x, 45.1f, 60f);
        Assert.Equal(stopped, released.RaycastWalkable(start, outside));
        Assert.Equal(stopped, production.RaycastWalkable(start, outside));
        var nearby = new Vector3(46f, 0f, 10f);
        Assert.Equal(nearby, retained.RaycastWalkable(start, nearby));
        Assert.Equal(nearby, released.RaycastWalkable(start, nearby));
        Assert.Equal(nearby, production.RaycastWalkable(start, nearby));

        float height = retained.SampleGroundHeight(45f, 3f, 10f);
        Assert.InRange(height, -0.1f, 0.5f);
        Assert.Equal(height, released.SampleGroundHeight(45f, 3f, 10f));
        Assert.Equal(height, production.SampleGroundHeight(45f, 3f, 10f));
    }

    private static MapNavigator Navigator(DtNavMesh mesh)
    {
        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        // Completion order assigns different tile refs. Normalize those refs before comparing path tie-breaks.
        navigator.LoadFromNavMesh(NavmeshBakeFixture.CanonicalMesh(mesh));
        return navigator;
    }

    private static Bakes Build()
    {
        var fixture = new NavmeshBakeFixture();
        return new Bakes(fixture.Bake(true).NavMesh, fixture.Bake(false).NavMesh, fixture.BakeProduction());
    }

    private sealed record Bakes(DtNavMesh Retained, DtNavMesh Released, DtNavMesh Production);
}
