namespace Avalon.World.Maps.Navigation;

/// <summary>What <see cref="IGroundNavigator.FindGround" /> found (#720).</summary>
public enum NavmeshGroundKind
{
    /// <summary>No navmesh is loaded; the ground given back is the point asked about, unchanged.</summary>
    NoNavMesh,

    /// <summary>The navmesh lies under the point: the ground keeps its X/Z, at the mesh's height.</summary>
    Under,

    /// <summary>
    /// The point is beside the navmesh, but within the search box of it: the ground is the nearest
    /// point of the mesh, so its X/Z differ from the point's.
    /// </summary>
    Nearest,

    /// <summary>No navmesh within the search box; the ground given back is the point asked about, unchanged.</summary>
    None,
}
