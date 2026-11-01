using System.Runtime.CompilerServices;
using Avalon.World.ChunkLayouts;

namespace Avalon.World.Instances;

/// <summary>
/// The cells an instance's indexes share (<see cref="SpatialGrid" />, the doodad index): a uniform grid on X/Z over the
/// layout's chunks, from the lowest chunk corner to the far side of the highest, one cell per chunk cell
/// (<see cref="ChunkLayout.CellSize" />), coarser for a layout far larger than any map. A coordinate is clamped to the
/// edge cells, so a position off the layout is in the nearest edge cell. A layout with no chunk or no usable cell size
/// is one cell.
/// </summary>
internal readonly struct GridCells
{
    // A layout far larger than any generated or authored map gets coarser cells rather than an array of this many.
    private const int MaxCells = 1 << 16;

    public readonly float OriginX;
    public readonly float OriginZ;
    public readonly float CellSize;
    public readonly float InverseCellSize;
    public readonly int Columns;
    public readonly int Rows;

    private GridCells(float originX, float originZ, float cellSize, int columns, int rows)
    {
        OriginX = originX;
        OriginZ = originZ;
        CellSize = cellSize;
        InverseCellSize = 1f / cellSize;
        Columns = columns;
        Rows = rows;
    }

    /// <summary>One cell holding everything: the cells of a store built with no layout.</summary>
    public static GridCells One { get; } = new(0f, 0f, 1f, 1, 1);

    /// <summary>How many cells there are.</summary>
    public int Count => Columns * Rows;

    /// <summary>The cells over <paramref name="layout" />'s chunks.</summary>
    public static GridCells For(ChunkLayout layout)
    {
        float cellSize = layout.CellSize;
        if (layout.Chunks.Count == 0 || !float.IsFinite(cellSize) || cellSize <= 0f)
            return One;

        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (PlacedChunk chunk in layout.Chunks)
        {
            minX = MathF.Min(minX, chunk.WorldPos.x);
            minZ = MathF.Min(minZ, chunk.WorldPos.z);
            maxX = MathF.Max(maxX, chunk.WorldPos.x);
            maxZ = MathF.Max(maxZ, chunk.WorldPos.z);
        }

        if (!float.IsFinite(minX) || !float.IsFinite(minZ) || !float.IsFinite(maxX) || !float.IsFinite(maxZ))
            return One;

        double width = (double)maxX - minX + cellSize;
        double depth = (double)maxZ - minZ + cellSize;
        while (Math.Ceiling(width / cellSize) * Math.Ceiling(depth / cellSize) > MaxCells)
            cellSize *= 2f;

        int columns = Math.Max(1, (int)Math.Ceiling(width / cellSize));
        int rows = Math.Max(1, (int)Math.Ceiling(depth / cellSize));
        return new GridCells(minX, minZ, cellSize, columns, rows);
    }

    /// <summary>
    /// The column of <paramref name="x" />, clamped to the edge columns. Monotonic in X, which is what lets a query find
    /// a cell range from the ends of its box or circle.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Column(float x)
    {
        float cell = (x - OriginX) * InverseCellSize;
        return cell <= 0f ? 0 : cell >= Columns - 1 ? Columns - 1 : (int)cell;
    }

    /// <summary>The row of <paramref name="z" />, clamped to the edge rows; monotonic in Z, as <see cref="Column" /> is in X.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Row(float z)
    {
        float cell = (z - OriginZ) * InverseCellSize;
        return cell <= 0f ? 0 : cell >= Rows - 1 ? Rows - 1 : (int)cell;
    }
}
