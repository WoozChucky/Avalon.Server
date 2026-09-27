using Avalon.Common.Mathematics;
using Avalon.Server.World.UnitTests.Creatures;
using Avalon.World.Maps.Navigation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Maps.Navigation;

/// <summary>
/// Unit tests for <see cref="MapNavigator"/>. Covers default-state (no mesh loaded)
/// behaviour for <c>RaycastWalkable</c> and <c>SampleGroundHeight</c>. Integration
/// tests against a real navmesh fixture should be added separately when a stable
/// test fixture .navmesh is committed.
/// </summary>
public class MapNavigatorShould
{
    private static MapNavigator BuildUnloaded()
    {
        // No LoadAsync called → _navMesh is null. Methods must handle this gracefully.
        return new MapNavigator(NullLoggerFactory.Instance);
    }

    [Fact]
    public void RaycastWalkable_returns_to_when_navmesh_unloaded()
    {
        var nav = BuildUnloaded();
        var from = new Vector3(0, 0, 0);
        var to = new Vector3(5, 0, 5);
        var result = nav.RaycastWalkable(from, to);
        Assert.Equal(to, result);
    }

    [Fact]
    public void SampleGroundHeight_returns_input_y_when_navmesh_unloaded()
    {
        var nav = BuildUnloaded();
        var y = nav.SampleGroundHeight(2.5f, 30f, 3.5f);
        Assert.Equal(30f, y);
    }

    private static MapNavigator BuildOverFlatGround()
    {
        var nav = new MapNavigator(NullLoggerFactory.Instance);
        nav.LoadFromNavMesh(CrowdLocomotionShould.FlatNavMesh.Value);
        return nav;
    }

    /// <summary>
    /// #638: the buffered FindPath refills a used list with exactly the route a fresh list gets, point
    /// for point, however long the route it held before.
    /// </summary>
    [Fact]
    public void FindPath_into_a_used_buffer_gives_the_same_route_as_a_new_list()
    {
        var nav = BuildOverFlatGround();
        var buffer = new List<Vector3>();
        nav.FindPath(new Vector3(-15f, 0f, -15f), new Vector3(15f, 0f, 15f), buffer);
        Assert.True(buffer.Count > 10);

        var start = new Vector3(-3f, 0f, 2f);
        var end = new Vector3(6f, 0f, -4f);
        nav.FindPath(start, end, buffer);

        List<Vector3> fresh = nav.FindPath(start, end);
        Assert.NotEmpty(fresh);
        Assert.Equal(fresh, buffer);
    }

    [Fact]
    public void FindPath_into_a_used_buffer_leaves_it_empty_when_there_is_no_route()
    {
        var nav = BuildOverFlatGround();
        var buffer = new List<Vector3>();
        nav.FindPath(new Vector3(-15f, 0f, -15f), new Vector3(15f, 0f, 15f), buffer);
        Assert.NotEmpty(buffer);

        nav.FindPath(new Vector3(0f, 0f, 0f), new Vector3(200f, 0f, 200f), buffer);

        Assert.Empty(buffer);
    }

    [Fact]
    public void FindPath_into_a_buffer_leaves_it_empty_when_navmesh_unloaded()
    {
        var nav = BuildUnloaded();
        var buffer = new List<Vector3> { new(1f, 2f, 3f) };

        nav.FindPath(Vector3.zero, new Vector3(5f, 0f, 5f), buffer);

        Assert.Empty(buffer);
    }
}
