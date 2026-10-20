using Avalon.Common.ValueObjects;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// Generates the scenery ring around a layout from its seed and the map's pool (#1110). Pure and deterministic: the
/// same layout and pool always give the same ring, and the ring draws from a random stream of its own, so the layout's
/// generation and the creature and doodad placement keyed to its seed are untouched.
/// </summary>
/// <remarks>
/// Ring cells are visited by GridX then GridZ, and each takes, in this priority:
/// <list type="number">
/// <item>a <b>cap</b> when open exits face it (<see cref="CappedSides" />): a cap whose capped sides turn exactly onto
/// the exits' sides, drawn by weight, avoiding any cap already placed within <see cref="CapSpacing" /> cells of it
/// (counting diagonals) when another of that shape is not, so one set piece does not close two exits in one view (a
/// shape with one piece repeats); where every piece of the shape stands that near, it avoids those within one cell,
/// then those west or south of it, and repeats a piece only when every piece of the shape stands west or south of it;</item>
/// <item>a <b>town-ward</b> piece on one of the 8 spots around the entry chunk, when the pool has them: that spot's
/// piece, never turned;</item>
/// <item>a <b>border</b>, drawn by weight, never the non-filler piece already west or south of it, with its front turned
/// toward the layout: the first of N, E, S, W that is a layout cell; else the first of NE, SE, SW, NW, faced through
/// N, E, S, W respectively (a corner faces its clockwise-first side); else, for a cell with no layout cell around it,
/// toward the nearest layout cell (fewest steps counting diagonals, ties by GridX then GridZ), north or south when it
/// is at least as far along Z as along X, else east or west.</item>
/// </list>
/// </remarks>
public static class SceneryRing
{
    /// <summary>How near (in cells, counting diagonals) a cap may not repeat a cap already placed, when its shape allows.</summary>
    private const int CapSpacing = 2;

    private static readonly int[] s_offsets = [-1, 0, 1, 2];

    private static readonly ExitSide[] s_sides = [ExitSide.N, ExitSide.E, ExitSide.S, ExitSide.W];

    private static readonly (int Dx, int Dz, ExitSide Faced)[] s_corners =
    [
        (1, 1, ExitSide.N),
        (1, -1, ExitSide.E),
        (-1, -1, ExitSide.S),
        (-1, 1, ExitSide.W),
    ];

    /// <summary>
    /// The ring around a cell set: every <c>(x + dx, z + dz)</c> with <c>dx, dz</c> in -1..2 over every layout cell, minus
    /// the layout cells, sorted by X then Z.
    /// </summary>
    public static IReadOnlyList<(int X, int Z)> Cells(IEnumerable<(int X, int Z)> layoutCells)
    {
        ArgumentNullException.ThrowIfNull(layoutCells);

        HashSet<(int X, int Z)> layout = [.. layoutCells];
        HashSet<(int X, int Z)> ring = [];
        foreach ((int x, int z) in layout)
        {
            foreach (int dx in s_offsets)
            {
                foreach (int dz in s_offsets)
                {
                    (int X, int Z) cell = (x + dx, z + dz);
                    if (!layout.Contains(cell))
                        ring.Add(cell);
                }
            }
        }

        return [.. ring.OrderBy(c => c.X).ThenBy(c => c.Z)];
    }

    /// <summary>
    /// The cells open exits face and, for each, the world sides the exits come through (a <see cref="ScenerySides" />
    /// mask): an exit is open when its side of the placed (turned) chunk has any exit slot and the cell beyond holds no
    /// chunk.
    /// </summary>
    public static IReadOnlyDictionary<(int X, int Z), byte> CappedSides(ChunkLayout layout, Func<ChunkTemplateId, ushort> exitsOf)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(exitsOf);

        HashSet<(int X, int Z)> cells = LayoutCells(layout);
        Dictionary<(int X, int Z), byte> capped = [];
        foreach (PlacedChunk chunk in layout.Chunks)
        {
            ushort exits = ExitMask.Rotate(exitsOf(chunk.TemplateId), chunk.Rotation);
            foreach (ExitSide side in s_sides)
            {
                if (((exits >> ((int)side * 3)) & 0b111) == 0)
                    continue;

                (int dx, int dz) = ExitMask.GridDir(side);
                (int X, int Z) faced = (chunk.GridX + dx, chunk.GridZ + dz);
                if (cells.Contains(faced))
                    continue;

                capped[faced] = (byte)(capped.GetValueOrDefault(faced) | ScenerySides.Bit(ExitMask.Opposite(side)));
            }
        }

        return capped;
    }

    /// <summary>One placement per ring cell of the layout (<see cref="Cells" />), sorted by GridX then GridZ.</summary>
    public static IReadOnlyList<SceneryPlacement> Build(ChunkLayout layout, Func<ChunkTemplateId, ushort> exitsOf, SceneryPool pool)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(exitsOf);
        ArgumentNullException.ThrowIfNull(pool);

        HashSet<(int X, int Z)> cells = LayoutCells(layout);
        IReadOnlyDictionary<(int X, int Z), byte> capped = CappedSides(layout, exitsOf);
        List<SceneryPoolPiece> caps = [.. pool.Pieces.Where(p => p.Kind == SceneryKind.Cap)];
        List<SceneryPoolPiece> borders = [.. pool.Pieces.Where(p => p.Kind == SceneryKind.Border)];
        Dictionary<(int X, int Z), SceneryPoolPiece> townWard = pool.Pieces
            .Where(p => p.Kind == SceneryKind.TownWard)
            .ToDictionary(p => (layout.EntryChunk.GridX + p.SpotX, layout.EntryChunk.GridZ + p.SpotZ));

        var rng = new Random(StreamSeed(layout.Seed));
        Dictionary<(int X, int Z), string> placed = [];
        List<SceneryPlacement> ring = [];
        foreach ((int X, int Z) cell in Cells(cells))
        {
            SceneryPlacement placement = capped.TryGetValue(cell, out byte sides)
                ? PlaceCap(cell, sides, caps, placed, rng)
                : townWard.TryGetValue(cell, out SceneryPoolPiece? spot)
                    ? new SceneryPlacement(spot.Name, cell.X, cell.Z, 0)
                    : PlaceBorder(cell, cells, borders, placed, rng);

            placed[cell] = placement.Name;
            ring.Add(placement);
        }

        return ring;
    }

    private static SceneryPlacement PlaceCap((int X, int Z) cell, byte sides, List<SceneryPoolPiece> caps,
        Dictionary<(int X, int Z), string> placed, Random rng)
    {
        // The pool holds a cap of every shape, so some cap always fits.
        // A cap avoids every piece within CapSpacing of it; where its shape's every piece stands that near, it avoids
        // those within one cell, then (reach 0) those west or south of it, and only then repeats a neighbour.
        List<SceneryPoolPiece> shaped = [.. caps.Where(p => Turns(p.CappedSides, sides).Count > 0)];
        List<SceneryPoolPiece> candidates = shaped;
        for (int reach = CapSpacing; reach >= 0; reach--)
        {
            List<SceneryPoolPiece> fresh = [.. shaped.Where(p => reach > 0 ? !IsNear(placed, cell, p.Name, reach) : !IsBeside(placed, cell, p.Name))];
            if (fresh.Count > 0)
            {
                candidates = fresh;
                break;
            }
        }

        SceneryPoolPiece piece = Draw(candidates, rng);
        List<int> turns = Turns(piece.CappedSides, sides);
        return new SceneryPlacement(piece.Name, cell.X, cell.Z, turns[rng.Next(turns.Count)]);
    }

    private static SceneryPlacement PlaceBorder((int X, int Z) cell, HashSet<(int X, int Z)> cells, List<SceneryPoolPiece> borders,
        Dictionary<(int X, int Z), string> placed, Random rng)
    {
        // The pool holds a filler, and a filler is never left out, so some border always fits.
        List<SceneryPoolPiece> candidates = [.. borders.Where(p => p.Filler || !IsBeside(placed, cell, p.Name))];
        SceneryPoolPiece piece = Draw(candidates, rng);
        ExitSide target = Facing(cell, cells);
        return new SceneryPlacement(piece.Name, cell.X, cell.Z, ((int)target - (int)piece.Front) & 0b11);
    }

    /// <summary>The quarter turns that put a cap's own capped sides exactly onto <paramref name="sides" />.</summary>
    private static List<int> Turns(byte capped, byte sides)
    {
        List<int> turns = [];
        for (int r = 0; r < 4; r++)
        {
            if (ScenerySides.Rotate(capped, r) == sides)
                turns.Add(r);
        }

        return turns;
    }

    /// <summary>Whether any cell within <paramref name="reach" /> of <paramref name="cell" /> (counting diagonals) holds the piece.</summary>
    private static bool IsNear(Dictionary<(int X, int Z), string> placed, (int X, int Z) cell, string name, int reach)
    {
        for (int dx = -reach; dx <= reach; dx++)
        {
            for (int dz = -reach; dz <= reach; dz++)
            {
                if (placed.TryGetValue((cell.X + dx, cell.Z + dz), out string? near) && near == name)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Whether the cell west or south of <paramref name="cell" />, both visited before it, holds the piece.</summary>
    private static bool IsBeside(Dictionary<(int X, int Z), string> placed, (int X, int Z) cell, string name) =>
        (placed.TryGetValue((cell.X - 1, cell.Z), out string? west) && west == name)
        || (placed.TryGetValue((cell.X, cell.Z - 1), out string? south) && south == name);

    /// <summary>A weighted draw: one number in [0, total weight), less each weight in order until it drops below 0.</summary>
    private static SceneryPoolPiece Draw(List<SceneryPoolPiece> pieces, Random rng)
    {
        double total = 0;
        foreach (SceneryPoolPiece piece in pieces)
            total += piece.Weight;

        double r = rng.NextDouble() * total;
        foreach (SceneryPoolPiece piece in pieces)
        {
            r -= piece.Weight;
            if (r < 0)
                return piece;
        }

        return pieces[^1];
    }

    /// <summary>The world side a border's front turns to (the class remarks give the order).</summary>
    private static ExitSide Facing((int X, int Z) cell, HashSet<(int X, int Z)> cells)
    {
        foreach (ExitSide side in s_sides)
        {
            (int dx, int dz) = ExitMask.GridDir(side);
            if (cells.Contains((cell.X + dx, cell.Z + dz)))
                return side;
        }

        foreach ((int dx, int dz, ExitSide faced) in s_corners)
        {
            if (cells.Contains((cell.X + dx, cell.Z + dz)))
                return faced;
        }

        (int X, int Z) nearest = cells
            .OrderBy(c => Math.Max(Math.Abs(c.X - cell.X), Math.Abs(c.Z - cell.Z)))
            .ThenBy(c => c.X)
            .ThenBy(c => c.Z)
            .First();
        int towardX = nearest.X - cell.X;
        int towardZ = nearest.Z - cell.Z;
        return Math.Abs(towardZ) >= Math.Abs(towardX)
            ? towardZ > 0 ? ExitSide.N : ExitSide.S
            : towardX > 0 ? ExitSide.E : ExitSide.W;
    }

    private static HashSet<(int X, int Z)> LayoutCells(ChunkLayout layout) => [.. layout.Chunks.Select(c => ((int)c.GridX, (int)c.GridZ))];

    /// <summary>
    /// The ring's own stream seed: the layout seed through the SplitMix64 finalizer over a fixed salt ("RINGSCEN" in ASCII),
    /// so the ring's draws share nothing with the layout's.
    /// </summary>
    private static int StreamSeed(int seed)
    {
        ulong z = (ulong)(uint)seed ^ 0x52494E475343454EUL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(uint)z;
    }
}
