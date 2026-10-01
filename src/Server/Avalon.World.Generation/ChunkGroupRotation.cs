namespace Avalon.World.ChunkLayouts;

/// <summary>
/// Where a set piece's cells land when the whole piece turns by quarter-turns, in ChunkRotation's sense (rotation 1
/// takes north to east). Each member turns by the same rotation about its own centre, which, with its cell moved this
/// way, is the same as turning the whole piece about its centre, so ChunkRotation and the client need no change.
/// </summary>
public static class ChunkGroupRotation
{
    public static (int X, int Z) RotateCell(int x, int z, int sizeX, int sizeZ, byte rotation) => (rotation & 3) switch
    {
        1 => (z, sizeX - 1 - x),
        2 => (sizeX - 1 - x, sizeZ - 1 - z),
        3 => (sizeZ - 1 - z, x),
        _ => (x, z),
    };
}
