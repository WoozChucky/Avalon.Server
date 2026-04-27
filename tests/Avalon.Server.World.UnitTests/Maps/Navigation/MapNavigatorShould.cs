using Avalon.Common.Mathematics;
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
}
