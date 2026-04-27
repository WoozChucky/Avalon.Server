using Avalon.Common.Mathematics;

namespace Avalon.World.Public.Maps;

public interface IMapNavigator
{
    Task LoadAsync(string meshFilename);
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
    /// Returns the navmesh ground height at column <paramref name="x"/>, <paramref name="z"/>.
    /// Returns 0f when no navmesh is loaded or the column is off-mesh.
    /// </summary>
    float SampleGroundHeight(float x, float z);

    object? Mesh { get; }
}
