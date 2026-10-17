using System.Diagnostics.CodeAnalysis;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.ChunkLayouts;

namespace Avalon.World.Doodads;

/// <summary>What a <see cref="DoodadIndex" /> walk is handed, one doodad at a time.</summary>
public interface IDoodadVisitor
{
    void Visit(Doodad doodad);
}

/// <summary>
/// An instance's doodads by where they stand: a uniform grid on X/Z over the layout's chunks, one cell per chunk cell, so
/// a view decision visits the doodads of the cells around its character instead of every doodad of the instance. Filled
/// as the instance is built, off the tick, and read-only once it is published, since doodads never move. World-side.
/// </summary>
/// <remarks>
/// The cells are those of the instance's spatial grid: from the lowest chunk corner to the far side of the highest,
/// coordinates clamped to the edge cells, so a doodad off the layout is in the nearest edge cell. A doodad whose X or Z is
/// not finite is listed in <see cref="All" /> but in no cell, as no distance finds it within reach.
/// </remarks>
public sealed class DoodadIndex
{
    // A layout far larger than any generated or authored map gets coarser cells rather than an array of this many.
    private const int MaxCells = 1 << 16;

    private readonly float _originX;
    private readonly float _originZ;
    private readonly float _inverseCellSize;
    private readonly int _columns;
    private readonly int _rows;

    // Each cell's doodads, the list made by the first doodad in it: a cell without one stays null.
    private readonly List<Doodad>?[] _cells;
    private readonly List<Doodad> _all = [];
    private readonly Dictionary<ulong, Doodad> _byGuid = [];

    private DoodadIndex(float originX, float originZ, float cellSize, int columns, int rows)
    {
        _originX = originX;
        _originZ = originZ;
        _inverseCellSize = 1f / cellSize;
        _columns = columns;
        _rows = rows;
        _cells = new List<Doodad>?[columns * rows];
    }

    /// <summary>
    /// The index over <paramref name="layout" />'s chunks, its cells those of the instance's spatial grid
    /// (<see cref="ChunkLayout.CellSize" />). A layout with no chunk or no usable cell size gets one cell.
    /// </summary>
    public static DoodadIndex For(ChunkLayout layout)
    {
        float cellSize = layout.CellSize;
        if (layout.Chunks.Count == 0 || !float.IsFinite(cellSize) || cellSize <= 0f)
            return new DoodadIndex(0f, 0f, 1f, 1, 1);

        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (PlacedChunk chunk in layout.Chunks)
        {
            minX = MathF.Min(minX, chunk.WorldPos.x);
            minZ = MathF.Min(minZ, chunk.WorldPos.z);
            maxX = MathF.Max(maxX, chunk.WorldPos.x);
            maxZ = MathF.Max(maxZ, chunk.WorldPos.z);
        }

        if (!float.IsFinite(minX) || !float.IsFinite(minZ) || !float.IsFinite(maxX) || !float.IsFinite(maxZ))
            return new DoodadIndex(0f, 0f, 1f, 1, 1);

        double width = (double)maxX - minX + cellSize;
        double depth = (double)maxZ - minZ + cellSize;
        while (Math.Ceiling(width / cellSize) * Math.Ceiling(depth / cellSize) > MaxCells)
            cellSize *= 2f;

        int columns = Math.Max(1, (int)Math.Ceiling(width / cellSize));
        int rows = Math.Max(1, (int)Math.Ceiling(depth / cellSize));
        return new DoodadIndex(minX, minZ, cellSize, columns, rows);
    }

    /// <summary>Every doodad here, in the order they were added.</summary>
    public IReadOnlyList<Doodad> All => _all;

    public int Count => _all.Count;

    /// <summary>
    /// Files <paramref name="doodad" /> at its position. Build time only, before the instance is published; a doodad never
    /// moves, so it is never filed again. Throws for a guid already here.
    /// </summary>
    public void Add(Doodad doodad)
    {
        _byGuid.Add(doodad.Guid.RawValue, doodad);
        _all.Add(doodad);

        Vector3 position = doodad.Position;
        if (!float.IsFinite(position.x) || !float.IsFinite(position.z))
            return;

        int cell = Row(position.z) * _columns + Column(position.x);
        (_cells[cell] ??= []).Add(doodad);
    }

    public bool TryGet(ObjectGuid guid, [NotNullWhen(true)] out Doodad? doodad) =>
        _byGuid.TryGetValue(guid.RawValue, out doodad);

    /// <summary>
    /// Hands <paramref name="visitor" /> every doodad within <paramref name="reach" /> of (x, z) on X/Z, each once: the
    /// doodads of the cells the circle's box overlaps, each kept only when its squared distance is at most the squared
    /// reach. A centre or a reach that is not a number, or a negative reach, finds none; an infinite reach finds every
    /// doodad in a cell. Allocates nothing.
    /// </summary>
    public void Visit<TVisitor>(float x, float z, float reach, ref TVisitor visitor)
        where TVisitor : struct, IDoodadVisitor
    {
        if (!float.IsFinite(x) || !float.IsFinite(z) || !(reach >= 0f) || _all.Count == 0)
            return;

        // The cells are found with the reach widened by a part in 1,024 and 0.25 m, far more than the rounding of a
        // distance or a cell index can be at any map's coordinates; the distance test below is the exact one. An infinite
        // reach widens to infinity, which the clamps turn into every cell.
        float widened = reach * (1f + 1f / 1024f) + 0.25f;
        float reachSquared = reach * reach;
        int firstColumn = Column(x - widened);
        int lastColumn = Column(x + widened);
        int lastRow = Row(z + widened);
        for (int row = Row(z - widened); row <= lastRow; row++)
        {
            int rowStart = row * _columns;
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                if (_cells[rowStart + column] is not { } doodads)
                    continue;

                for (int i = 0; i < doodads.Count; i++)
                {
                    Doodad doodad = doodads[i];
                    Vector3 position = doodad.Position;
                    float dx = position.x - x;
                    float dz = position.z - z;
                    if (dx * dx + dz * dz <= reachSquared)
                        visitor.Visit(doodad);
                }
            }
        }
    }

    // Monotonic in X and in Z, as the spatial grid's, which is what lets a query find a cell range from its box's ends.
    private int Column(float x)
    {
        float cell = (x - _originX) * _inverseCellSize;
        return cell <= 0f ? 0 : cell >= _columns - 1 ? _columns - 1 : (int)cell;
    }

    private int Row(float z)
    {
        float cell = (z - _originZ) * _inverseCellSize;
        return cell <= 0f ? 0 : cell >= _rows - 1 ? _rows - 1 : (int)cell;
    }
}
