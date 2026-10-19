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
            bounds ??= KitBounds.Load();
            if (!bounds.ContainsKey(item.Model))
                throw new InvalidOperationException($"{prefix}: the kit's bounds table has no model '{item.Model}'");
            if (!FitsInCell(item, bounds))
                throw new InvalidOperationException($"{at} crosses the edge of {CellName(prefix, cell.X, cell.Z)}");
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
        foreach ((float x, float z) in new[] { (box.MinX, box.MinZ), (box.MaxX, box.MinZ), (box.MaxX, box.MaxZ), (box.MinX, box.MaxZ) })
        {
            (float tx, float tz) = Yaw.Turn(x * item.Scale, z * item.Scale, item.Yaw);
            if (!Inside(item.X + tx - ox, -0.001f) || !Inside(item.Z + tz - oz, -0.001f)) return false;
        }
        return true;
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
