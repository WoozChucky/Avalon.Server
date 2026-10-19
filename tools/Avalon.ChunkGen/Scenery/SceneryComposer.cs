using System.Globalization;
using Polygon = System.Collections.Generic.IReadOnlyList<(float X, float Y, float Z)>;

namespace Avalon.ChunkGen;

/// <summary>
/// Cuts one scene composed in world metres (cell (x, z) spans x * 30 to x * 30 + 30 on each axis) into a scenery piece
/// per cell, so a stream or a road runs across cells and still meets itself at every edge.
/// </summary>
public static class SceneryComposer
{
    /// <summary>
    /// A piece for every cell, named <see cref="CellName" />, even an empty one, carrying its cell (GridX, GridZ) and
    /// <paramref name="kit" />, then passed through <paramref name="describe" /> (its kind, family and the rest). Each
    /// cell's terrain is the world's height and material shifted into it; each surface is clipped to the cell (a
    /// surface with nothing there is left out); each wall goes whole to the cell it stands in; each decor item moves into
    /// the cell holding its position. Refuses (InvalidOperationException) a cell listed twice, a wall whose rough face
    /// would cross a cell edge (only the ground and the surfaces are cut), a wall or an item in no listed cell, and an
    /// item whose turned and scaled kit box crosses its cell's edge (<c>"crosses the edge of &lt;name&gt;"</c>), so
    /// scatter code tests <see cref="FitsInCell" /> before adding.
    /// </summary>
    public static IReadOnlyList<SceneryPiece> Cut(
        string prefix, IEnumerable<(int X, int Z)> cells, SceneryTerrain world, IReadOnlyList<DecorItem> worldDecor, string kit,
        Func<(int X, int Z), SceneryPiece, SceneryPiece>? describe = null)
    {
        List<(int X, int Z)> order = [.. cells];
        var walls = new Dictionary<(int X, int Z), List<SceneryWall>>();
        var decor = new Dictionary<(int X, int Z), List<DecorItem>>();
        foreach ((int X, int Z) cell in order)
        {
            if (!walls.TryAdd(cell, []))
                throw new InvalidOperationException($"{prefix}: cell ({I(cell.X)}, {I(cell.Z)}) is listed twice");
            decor[cell] = [];
        }

        for (int w = 0; w < world.Walls.Count; w++)
        {
            SceneryWall wall = world.Walls[w];
            if (wall.Outline.Count == 0) throw new InvalidOperationException($"{prefix}: wall {I(w + 1)} has no outline");
            (int X, int Z) cell = CellOf(wall.Outline[0].X, wall.Outline[0].Z);
            float ox = cell.X * ChunkPiece.CellSize, oz = cell.Z * ChunkPiece.CellSize;
            if (!wall.Outline.All(p => Inside(p.X - ox, SceneryWall.Roughness) && Inside(p.Z - oz, SceneryWall.Roughness)))
                throw new InvalidOperationException($"{prefix}: wall {I(w + 1)} crosses the edge of {CellName(prefix, cell.X, cell.Z)}");
            if (!walls.TryGetValue(cell, out List<SceneryWall>? held))
                throw new InvalidOperationException($"{prefix}: wall {I(w + 1)} stands in cell ({I(cell.X)}, {I(cell.Z)}), which is not cut");
            held.Add(wall with { Outline = [.. wall.Outline.Select(p => (p.X - ox, p.Z - oz))] });
        }

        IReadOnlyDictionary<string, ModelBounds>? bounds = null;
        foreach (DecorItem item in worldDecor)
        {
            (int X, int Z) cell = CellOf(item.X, item.Z);
            string at = $"{prefix}: {item.Model} at ({ForestText.N(item.X)}, {ForestText.N(item.Z)})";
            if (!decor.TryGetValue(cell, out List<DecorItem>? held))
                throw new InvalidOperationException($"{at} stands in cell ({I(cell.X)}, {I(cell.Z)}), which is not cut");
            string name = CellName(prefix, cell.X, cell.Z);
            bool fits;
            try
            {
                fits = FitsInCell(item, bounds ??= KitBounds.Load());
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidOperationException($"{name}: {e.Message}", e);
            }
            if (!fits)
                throw new InvalidOperationException($"{at} crosses the edge of {name}");
            held.Add(item with { X = item.X - cell.X * ChunkPiece.CellSize, Z = item.Z - cell.Z * ChunkPiece.CellSize });
        }

        List<SceneryPiece> pieces = [];
        foreach ((int X, int Z) cell in order)
        {
            float ox = cell.X * ChunkPiece.CellSize, oz = cell.Z * ChunkPiece.CellSize;
            List<ScenerySurface> surfaces = [];
            foreach (ScenerySurface surface in world.Surfaces)
            {
                IReadOnlyList<Polygon> within = surface.Within(ox, oz, ox + ChunkPiece.CellSize, oz + ChunkPiece.CellSize);
                if (within.Count > 0)
                    surfaces.Add(ScenerySurface.Of(surface.Material, [.. within.Select(p => Moved(p, -ox, -oz))]));
            }

            var terrain = new SceneryTerrain((x, z) => world.Height(x + ox, z + oz), (x, z) => world.Material(x + ox, z + oz))
            {
                Walls = walls[cell],
                Surfaces = surfaces,
            };
            var piece = new SceneryPiece(CellName(prefix, cell.X, cell.Z), terrain, decor[cell]) { GridX = cell.X, GridZ = cell.Z, Kit = kit };
            pieces.Add(describe is null ? piece : describe(cell, piece));
        }
        return pieces;
    }

    /// <summary>
    /// Whether the item's kit box, scaled and turned by its yaw, lies inside the 30 x 30 m cell holding its position
    /// (to 0.001 m), in world or piece-local metres alike. Refuses (InvalidOperationException) a model the table lacks.
    /// </summary>
    public static bool FitsInCell(DecorItem item, IReadOnlyDictionary<string, ModelBounds> kit)
    {
        if (!kit.TryGetValue(item.Model, out ModelBounds? box))
            throw new InvalidOperationException($"the kit's bounds table has no model '{item.Model}'");
        (int X, int Z) cell = CellOf(item.X, item.Z);
        float ox = cell.X * ChunkPiece.CellSize, oz = cell.Z * ChunkPiece.CellSize;
        (float minX, float maxX, float minZ, float maxZ) = TurnedBox(item, box);
        return Inside(minX - ox, -0.001f) && Inside(maxX - ox, -0.001f) && Inside(minZ - oz, -0.001f) && Inside(maxZ - oz, -0.001f);
    }

    /// <summary>
    /// The item's kit box scaled by its scale, turned by its yaw (Yaw.Turn on the box's four corners) and moved to its
    /// position: the turned corners' bounds on x and z, in the item's own metres (world or piece-local).
    /// </summary>
    internal static (float MinX, float MaxX, float MinZ, float MaxZ) TurnedBox(DecorItem item, ModelBounds box)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach ((float x, float z) in new[] { (box.MinX, box.MinZ), (box.MaxX, box.MinZ), (box.MaxX, box.MaxZ), (box.MinX, box.MaxZ) })
        {
            (float tx, float tz) = Yaw.Turn(x * item.Scale, z * item.Scale, item.Yaw);
            minX = Math.Min(minX, item.X + tx);
            maxX = Math.Max(maxX, item.X + tx);
            minZ = Math.Min(minZ, item.Z + tz);
            maxZ = Math.Max(maxZ, item.Z + tz);
        }
        return (minX, maxX, minZ, maxZ);
    }

    /// <summary><c>&lt;prefix&gt;_&lt;x&gt;_&lt;z&gt;</c>, a negative coordinate written <c>m&lt;abs&gt;</c> (<c>town_ring_m1_3</c>).</summary>
    public static string CellName(string prefix, int x, int z) => $"{prefix}_{Coordinate(x)}_{Coordinate(z)}";

    private static string Coordinate(int value) => value < 0 ? $"m{I(-value)}" : I(value);

    private static Polygon Moved(Polygon polygon, float dx, float dz) => [.. polygon.Select(v => (v.X + dx, v.Y, v.Z + dz))];

    private static (int X, int Z) CellOf(float x, float z) =>
        ((int)MathF.Floor(x / ChunkPiece.CellSize), (int)MathF.Floor(z / ChunkPiece.CellSize));

    /// <summary>
    /// Whether a cell-local coordinate lies at least <paramref name="margin" /> inside the cell (a negative margin allows
    /// that much over).
    /// </summary>
    private static bool Inside(float local, float margin) => local >= margin && local <= ChunkPiece.CellSize - margin;

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
}
