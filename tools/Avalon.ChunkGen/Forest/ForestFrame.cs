namespace Avalon.ChunkGen;

/// <summary>
/// An exit of a forest frame: the midpoint of its side, where its path ends; the direction into the frame; and its
/// throat (10 m wide, 6 m deep, centred on the side), which holds no blocker.
/// </summary>
internal sealed record ForestExit(Side Side, float X, float Z, float InX, float InZ, float MinX, float MaxX, float MinZ, float MaxZ)
{
    /// <summary>The yaw a path segment runs into the frame with: its model's length lies along +Z at yaw 0.</summary>
    public int InwardYaw => Side switch { Side.S => 0, Side.W => 90, Side.N => 180, _ => 270 };

    public bool InThroat(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

    /// <summary>The exit on <paramref name="side" /> of cell (cellX, cellZ) of a frame.</summary>
    public static ForestExit Of(Side side, int cellX, int cellZ)
    {
        const float Cell = ChunkPiece.CellSize, Half = Cell / 2f;
        float ox = cellX * Cell, oz = cellZ * Cell;
        return side switch
        {
            Side.N => new(side, ox + Half, oz + Cell, 0f, -1f, ox + 10f, ox + 20f, oz + 24f, oz + Cell),
            Side.S => new(side, ox + Half, oz, 0f, 1f, ox + 10f, ox + 20f, oz, oz + 6f),
            Side.E => new(side, ox + Cell, oz + Half, -1f, 0f, ox + 24f, ox + Cell, oz + 10f, oz + 20f),
            _ => new(side, ox, oz + Half, 1f, 0f, ox, ox + 6f, oz + 10f, oz + 20f),
        };
    }
}

/// <summary>
/// What the forest dresser dresses and the forest decor rules check: a single piece in its 30 x 30 m cell, or a set
/// piece in its 60 x 60 m frame, with its blockers, exits, slots and hints in that frame.
/// </summary>
internal sealed record ForestFrame(
    string Name, float Size, IReadOnlyList<Blocker> Blockers, IReadOnlyList<ForestExit> Exits, IReadOnlyList<DoodadSlot> DoodadSlots,
    DressingHints Hints)
{
    /// <summary>Where the frame's paths meet: the hint's point, or the frame's centre.</summary>
    public float MeetX => Hints.MeetX ?? Size / 2f;

    public float MeetZ => Hints.MeetZ ?? Size / 2f;

    public static ForestFrame Of(ChunkPiece piece) =>
        new(piece.Name, ChunkPiece.CellSize, piece.Blockers, [.. piece.Exits.Select(e => ForestExit.Of(e, 0, 0))], piece.DoodadSlots, piece.Hints);

    public static ForestFrame Of(ChunkGroupPiece group) =>
        new(group.Name, 2 * ChunkPiece.CellSize, group.Blockers, [.. group.OuterExits.Select(e => ForestExit.Of(e.Side, e.CellX, e.CellZ))],
            group.DoodadSlots, group.Hints);
}

/// <summary>
/// Turns by whole-degree yaws, clockwise seen from above as ChunkRotation turns a chunk: a local (x, z) goes to
/// (x cos + z sin, z cos - x sin). The sines and cosines are rounded to 9 decimals, so every platform's maths library
/// gives the dresser the same numbers and a run is byte-identical on any machine.
/// </summary>
internal static class Yaw
{
    private static readonly (float Sin, float Cos)[] s_table = [.. Enumerable.Range(0, 360).Select(d =>
    {
        double radians = d * Math.PI / 180.0;
        return ((float)Math.Round(Math.Sin(radians), 9), (float)Math.Round(Math.Cos(radians), 9));
    })];

    /// <summary>The yaw brought into 0-359.</summary>
    public static int Normal(int degrees) => ((degrees % 360) + 360) % 360;

    public static (float Sin, float Cos) Of(int degrees) => s_table[Normal(degrees)];

    /// <summary>A model-space (x, z) turned by the yaw.</summary>
    public static (float X, float Z) Turn(float x, float z, int degrees)
    {
        (float sin, float cos) = Of(degrees);
        return (x * cos + z * sin, z * cos - x * sin);
    }

    /// <summary>The direction a model's +Z points at the yaw.</summary>
    public static (float X, float Z) Forward(int degrees) => Turn(0f, 1f, degrees);
}
