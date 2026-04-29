using Avalon.Common.Mathematics;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// Single source of truth for the chunk-rotation case table used by the navmesh bake,
/// portal/spawn world placement, and the client-side mirror. Rotations pivot on the
/// CHUNK CENTER (cellSize/2, cellSize/2) so a 1×1 chunk's footprint stays inside its
/// declared cell after any 90° rotation. (Authoring places the chunk root at the SW
/// corner; rotating about the SW corner shifts the footprint into a neighbour cell —
/// causes overlap with the next chunk on the grid.)
///
/// Multi-cell footprints (CellFootprintX/Z &gt; 1) are not yet supported by the procedural
/// generator; when added, the pivot becomes (footprintX*cellSize/2, footprintZ*cellSize/2).
/// </summary>
public static class ChunkRotation
{
    /// <summary>
    /// Rotates a chunk-local (x, z) point around the chunk centre and translates by
    /// <paramref name="origin"/> (the world position of the chunk's SW corner). Works
    /// for both bake vertices and spawn/portal slot positions.
    /// </summary>
    public static Vector3 LocalToWorld(float localX, float localY, float localZ, byte rotation, float cellSize, Vector3 origin)
    {
        var c = cellSize * 0.5f;
        var lx = localX - c;
        var lz = localZ - c;
        (float rx, float rz) = rotation switch
        {
            0 => (lx, lz),
            1 => (lz, -lx),
            2 => (-lx, -lz),
            3 => (-lz, lx),
            _ => (lx, lz),
        };
        return new Vector3(origin.x + c + rx, origin.y + localY, origin.z + c + rz);
    }
}
