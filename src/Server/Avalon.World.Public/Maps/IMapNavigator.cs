using Avalon.Common.Mathematics;

namespace Avalon.World.Public.Maps;

public interface IMapNavigator
{
    List<Vector3> FindPath(Vector3 start, Vector3 end);
    bool HasVisibility(Vector3 start, Vector3 end);

    /// <summary>
    /// Raycast from <paramref name="from"/> toward <paramref name="to"/>, clamped to walkable
    /// navmesh surface. Returns the furthest point along the segment that stays walkable.
    /// If the segment is fully walkable, returns <paramref name="to"/>; if blocked, returns
    /// the closest point at the obstruction. Returns <paramref name="to"/> unchanged when
    /// no navmesh is loaded.
    /// </summary>
    Vector3 RaycastWalkable(Vector3 from, Vector3 to);

    /// <summary>
    /// Returns the navmesh ground height at column <paramref name="x"/>, <paramref name="z"/>,
    /// using <paramref name="y"/> as the search-box centre on the vertical axis. Pass the
    /// caller's current Y so multi-storey maps return the correct floor (the search box has
    /// limited vertical range; a wrong Y will miss the poly entirely).
    /// Returns <paramref name="y"/> unchanged when no navmesh is loaded, the column is
    /// off-mesh, or the height query fails.
    /// </summary>
    float SampleGroundHeight(float x, float y, float z);

    object? Mesh { get; }
}
