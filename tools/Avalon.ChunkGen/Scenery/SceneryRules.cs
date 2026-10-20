using System.Globalization;
using Avalon.World.ChunkLayouts;

namespace Avalon.ChunkGen;

/// <summary>
/// What ChunkGen refuses in scenery (scenery ring, #1110), as the decor rules refuse a chunk's decor. A piece's decor
/// stays inside its cell, and what stands near walkable floor is no bigger than the forest's own trees, so nothing hangs
/// over a path at the game camera's zoom. A forest piece meets every edge at y 0 on forest floor and carries a barrier
/// all along its 6 m edge band, and a cap keeps its clearing. The town's south and west cells keep low near the wall,
/// between the camera and the player. A forest pool holds every family and cap shape, and the town ring covers its 21
/// cells and meets the town and itself. A surface laid over the ground (a road's dirt) never dips under it. Every refusal is an InvalidOperationException naming the piece, the pool or the
/// ring.
/// </summary>
public static class SceneryRules
{
    /// <summary>The band along each cell edge, in metres, where scenery stands beside walkable floor.</summary>
    public const float EdgeBand = 6f;

    /// <summary>An item drawn taller than this, in metres, is tall decor: in the edge band it stays at chunk-tree size.</summary>
    public const float TallItem = 2.2f;

    /// <summary>The tallest an edge-band item other than a kit tree may be drawn, in metres (the dresser's largest rock fits).</summary>
    public const float EdgeBandRockTop = 4f;

    /// <summary>The radius, in metres, kept clear of trees around each capped edge's midpoint, so the cap reads from the camera.</summary>
    public const float CapClearing = 10f;

    /// <summary>Each edge carries a barrier item along every run of this many metres.</summary>
    public const float BarrierSpacing = 3f;

    /// <summary>Within this many metres of the town's wall, a south or west ring cell's decor stays below <see cref="TownWallNearTop" />.</summary>
    public const float TownWallNear = 4f;

    /// <summary>The wall's height, in metres: nothing near it on the camera's side stands taller.</summary>
    public const float TownWallNearTop = 2f;

    /// <summary>Within this many metres of the town's wall, a south or west ring cell's decor stays below <see cref="TownWallFarTop" />.</summary>
    public const float TownWallFar = 10f;

    /// <summary>The tallest a south or west ring cell's decor stands within <see cref="TownWallFar" /> of the wall, in metres.</summary>
    public const float TownWallFarTop = 6f;

    /// <summary>How far an item's box may reach past its cell, in metres.</summary>
    private const float CellTolerance = 0.01f;

    /// <summary>How far an edge vertex may stand off the height it must meet, in metres.</summary>
    private const float GroundTolerance = 0.0005f;

    private static readonly Lazy<IReadOnlyList<(int X, int Z)>> s_townCells =
        new(() => [.. TownPieces.Squares().Select(s => (s.GridX, s.GridZ))], isThreadSafe: true);

    private const string ForestFloor = "forest_floor";

    private const string AncientTree = "avalon_tree_ancient_";

    private static readonly Side[] s_sides = [Side.N, Side.E, Side.S, Side.W];

    /// <summary>The town's cells, as TownPieces lays its squares out: what the town ring surrounds and its pieces' rules measure from.</summary>
    internal static IReadOnlyList<(int X, int Z)> TownCells => s_townCells.Value;

    /// <summary>What reads as impassable along an edge, besides the kit trees: undergrowth, rocks, rims, walls and fences.</summary>
    private static readonly string[] s_barrierPrefixes =
    [
        "Bush_", "Fern_", "Plant_1_Big", "Rock_Medium_", "avalon_rock_", "avalon_boulder_", "avalon_cliff_", "avalon_ravine_lip_",
        "avalon_bramble_", "avalon_field_wall_", "avalon_fence_broken_", "avalon_giant_fallen_", "avalon_log_fallen_", "avalon_hedge_",
        "avalon_wheat_", "Prop_WoodenFence_",
    ];

    private static readonly string[] s_borderFamilies = ["cliffs", "ravine", "giant", "stream"];

    private static readonly string[] s_singleCapFamilies = ["rockfall", "trunk", "thicket", "brink"];

    /// <summary>
    /// A forest piece (border, cap or town-ward): every item a model of its kit (<see cref="DecorItem.Check" />, any
    /// whole degree) whose turned box lies within its cell; tall items in the edge band at chunk-tree size; the ground
    /// at y 0 on forest floor all round its edge; a barrier item for every <see cref="BarrierSpacing" /> metres of each
    /// edge; no tree within <see cref="CapClearing" /> of a capped edge's midpoint; and every surface laid over the
    /// ground at or above it (<see cref="CheckOverlays" />).
    /// </summary>
    public static void CheckForestPiece(SceneryPiece piece, IReadOnlyDictionary<string, ModelBounds> kit)
    {
        List<Footprint> footprints = [];
        foreach (DecorItem item in piece.Decor)
        {
            Footprint footprint = CheckItem(piece, item, kit);
            CheckEdgeBand(piece, footprint, s_sides);
            footprints.Add(footprint);
        }

        CheckForestGround(piece);
        CheckBarrier(piece, footprints);
        CheckCaps(piece);
        CheckOverlays(piece);
    }

    /// <summary>
    /// A town ring piece: every item held to <see cref="CheckTownItem" />, and every surface laid over the ground on it
    /// (<see cref="CheckOverlays" />).
    /// </summary>
    public static void CheckTownPiece(SceneryPiece piece, IReadOnlyDictionary<string, ModelBounds> kit)
    {
        foreach (DecorItem item in piece.Decor)
            CheckTownItem(piece, item, kit);
        CheckOverlays(piece);
    }

    /// <summary>
    /// One item of a town ring piece (piece-local): a model of its kit whose turned box lies within its cell; drawn
    /// taller than <see cref="TallItem" /> within <see cref="EdgeBand" /> of an edge the piece shares with the town, at
    /// chunk-tree size; and in a cell south or west of the town, nothing within <see cref="TownWallNear" /> of the town
    /// (<see cref="TownCells" />) drawn taller than <see cref="TownWallNearTop" />, nor within <see cref="TownWallFar" />
    /// drawn taller than <see cref="TownWallFarTop" />. The town ring's scene holds each item it places to this before
    /// adding it, so its scattering never builds a piece this refuses.
    /// </summary>
    internal static void CheckTownItem(SceneryPiece piece, DecorItem item, IReadOnlyDictionary<string, ModelBounds> kit)
    {
        IReadOnlyList<(int X, int Z)> town = TownCells;
        Footprint footprint = CheckItem(piece, item, kit);
        if (piece.GridX < town.Min(c => c.X) || piece.GridZ < town.Min(c => c.Z))   // south or west of the town
        {
            float distance = town.Min(cell => DistanceToCell(piece, footprint, cell));
            if (distance < TownWallNear && footprint.Top > TownWallNearTop)
                throw new InvalidOperationException(TooTall(piece, footprint, TownWallNearTop, TownWallNear, distance));
            if (distance < TownWallFar && footprint.Top > TownWallFarTop)
                throw new InvalidOperationException(TooTall(piece, footprint, TownWallFarTop, TownWallFar, distance));
        }
        CheckEdgeBand(piece, footprint, [.. s_sides.Where(s => town.Contains(Neighbour((piece.GridX, piece.GridZ), s)))]);
    }

    /// <summary>
    /// Every surface other than water and mist (dirt or forest floor laid over the ground) lies at or above the drawn
    /// ground (SceneryObjWriter's 1 m triangles over the terrain's heights) everywhere on it, within
    /// <see cref="OverlayTolerance" />: a surface under the ground is hidden where it should show. Both are planar per
    /// triangle, so their difference is least at a surface triangle's corners, where its edges cross the ground's grid
    /// lines (x, z and x - z whole) and at the grid's vertices inside it; every one of those is sampled.
    /// </summary>
    private static void CheckOverlays(SceneryPiece piece)
    {
        int cells = (int)MathF.Round(ChunkPiece.CellSize / SceneryTerrain.Step);
        float[,]? ground = null;
        foreach (ScenerySurface surface in piece.Terrain.Surfaces)
        {
            if (surface.Material is "water" or "mist") continue;
            ground ??= GroundHeights(piece, cells);
            foreach (IReadOnlyList<(float X, float Y, float Z)> polygon in surface.Within(0f, 0f, ChunkPiece.CellSize, ChunkPiece.CellSize))
            {
                for (int k = 1; k + 1 < polygon.Count; k++)
                {
                    (float X, float Y, float Z) a = polygon[0], b = polygon[k], c = polygon[k + 1];
                    foreach ((float x, float y, float z) in Samples(a, b, c))
                    {
                        float under = GroundAt(ground, cells, x, z) - y;
                        if (under > OverlayTolerance)
                        {
                            throw new InvalidOperationException(
                                $"{piece.Name}: its {surface.Material} surface at ({N(x)}, {N(z)}) lies {N(under)} m under the ground: it would not show");
                        }
                    }
                }
            }
        }
    }

    /// <summary>How far a surface laid over the ground may dip under it, in metres: the files' millimetre rounding.</summary>
    private const float OverlayTolerance = 0.002f;

    private static float[,] GroundHeights(SceneryPiece piece, int cells)
    {
        float[,] heights = new float[cells + 1, cells + 1];
        for (int i = 0; i <= cells; i++)
        {
            for (int j = 0; j <= cells; j++)
                heights[i, j] = piece.Terrain.Height(i * SceneryTerrain.Step, j * SceneryTerrain.Step);
        }
        return heights;
    }

    /// <summary>The drawn ground's height at a piece-local point: its quad's triangle, split along (x0, z0)-(x1, z1).</summary>
    private static float GroundAt(float[,] heights, int cells, float x, float z)
    {
        float gx = Math.Clamp(x / SceneryTerrain.Step, 0f, cells), gz = Math.Clamp(z / SceneryTerrain.Step, 0f, cells);
        int i = Math.Min((int)MathF.Floor(gx), cells - 1), j = Math.Min((int)MathF.Floor(gz), cells - 1);
        float fx = gx - i, fz = gz - j;
        float h00 = heights[i, j], h10 = heights[i + 1, j], h01 = heights[i, j + 1], h11 = heights[i + 1, j + 1];
        return fx >= fz ? h00 + fx * (h10 - h00) + fz * (h11 - h10) : h00 + fz * (h01 - h00) + fx * (h11 - h01);
    }

    /// <summary>
    /// The points of a surface triangle where its height over the ground can be least: its corners, its edges' crossings
    /// of the ground's grid lines, and the grid's vertices inside it, each with the triangle's height there.
    /// </summary>
    private static IEnumerable<(float X, float Y, float Z)> Samples((float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c)
    {
        float area = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
        if (MathF.Abs(area) < 1e-6f) yield break;
        yield return a;
        yield return b;
        yield return c;
        foreach (((float X, float Y, float Z) p, (float X, float Y, float Z) q) in new[] { (a, b), (b, c), (c, a) })
        {
            foreach (Func<(float X, float Y, float Z), float> line in new Func<(float X, float Y, float Z), float>[] { v => v.X, v => v.Z, v => v.X - v.Z })
            {
                float from = line(p), to = line(q);
                for (float k = MathF.Floor(MathF.Min(from, to)) + 1f; k < MathF.Max(from, to); k += 1f)
                {
                    float t = (k - from) / (to - from);
                    yield return (p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t, p.Z + (q.Z - p.Z) * t);
                }
            }
        }
        for (float x = MathF.Ceiling(MathF.Min(a.X, MathF.Min(b.X, c.X))); x <= MathF.Max(a.X, MathF.Max(b.X, c.X)); x += 1f)
        {
            for (float z = MathF.Ceiling(MathF.Min(a.Z, MathF.Min(b.Z, c.Z))); z <= MathF.Max(a.Z, MathF.Max(b.Z, c.Z)); z += 1f)
            {
                float u = ((b.X - x) * (c.Z - z) - (c.X - x) * (b.Z - z)) / area, v = ((c.X - x) * (a.Z - z) - (a.X - x) * (c.Z - z)) / area;
                float w = 1f - u - v;
                if (u >= -1e-5f && v >= -1e-5f && w >= -1e-5f) yield return (x, u * a.Y + v * b.Y + w * c.Y, z);
            }
        }
    }

    /// <summary>
    /// The town ring as a whole: one piece on every cell of <see cref="SceneryRing.Cells" /> around
    /// <paramref name="townCells" /> and none elsewhere; the ground at y 0 along every edge a piece shares with the town;
    /// and the ground at the same height on both sides of every edge two ring pieces share (the ring is one scene, so a
    /// seam between its own cells need not lie at y 0).
    /// </summary>
    public static void CheckTownRing(IReadOnlyList<SceneryPiece> pieces, IReadOnlyList<(int X, int Z)> townCells)
    {
        HashSet<(int X, int Z)> town = [.. townCells];
        IReadOnlyList<(int X, int Z)> ring = SceneryRing.Cells(town);
        HashSet<(int X, int Z)> ringCells = [.. ring];
        var byCell = new Dictionary<(int X, int Z), SceneryPiece>();
        foreach (SceneryPiece piece in pieces)
        {
            (int X, int Z) cell = (piece.GridX, piece.GridZ);
            if (!ringCells.Contains(cell))
                throw new InvalidOperationException($"{piece.Name} stands on cell ({I(cell.X)}, {I(cell.Z)}), which is not a town ring cell");
            if (!byCell.TryAdd(cell, piece))
                throw new InvalidOperationException($"{piece.Name} is a second piece for ring cell ({I(cell.X)}, {I(cell.Z)}), with {byCell[cell].Name}");
        }
        foreach ((int X, int Z) cell in ring)
        {
            if (!byCell.ContainsKey(cell))
                throw new InvalidOperationException($"the town ring has no piece for ring cell ({I(cell.X)}, {I(cell.Z)})");
        }

        foreach (SceneryPiece piece in pieces)
        {
            foreach (Side side in s_sides)
            {
                (int X, int Z) next = Neighbour((piece.GridX, piece.GridZ), side);
                if (town.Contains(next))
                {
                    foreach ((float x, float z) in EdgeVertices(side))
                    {
                        float y = piece.Terrain.Height(x, z);
                        if (!(MathF.Abs(y) <= GroundTolerance))
                        {
                            throw new InvalidOperationException(
                                $"{piece.Name}: its ground at ({N(x)}, {N(z)}) meets the town at y {N(y)}, not 0: a step at the wall");
                        }
                    }
                }
                else if ((side is Side.N or Side.E) && byCell.TryGetValue(next, out SceneryPiece? other))
                {
                    // The edge seen from the other side: N meets the other's S (z 30 to 0), E its W (x 30 to 0).
                    foreach ((float x, float z) in EdgeVertices(side))
                    {
                        float y = piece.Terrain.Height(x, z);
                        float across = side == Side.N ? other.Terrain.Height(x, 0f) : other.Terrain.Height(0f, z);
                        if (!(MathF.Abs(y - across) <= GroundTolerance))
                        {
                            throw new InvalidOperationException(
                                $"{piece.Name} and {other.Name} do not meet: at ({N(x)}, {N(z)}) on its {side} edge the ground is at y {N(y)}, "
                                + $"across it at y {N(across)}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// A forest pool: at least 3 old-woods borders (<c>woods</c>), every one a filler; a border of each of
    /// <c>cliffs</c>, <c>ravine</c>, <c>giant</c> and <c>stream</c>; a cap with exactly one capped side of each of
    /// <c>rockfall</c>, <c>trunk</c>, <c>thicket</c> and <c>brink</c>; and its ring file (SceneryJsonWriter.WritePool)
    /// read back by the server's reader (SceneryFile.Read), which refuses a missing cap shape or town-ward spot: its
    /// refusal is passed on with the same message. Returns that ring file's text, the one ChunkFiles.ForestScenery writes.
    /// </summary>
    public static string CheckForestPool(IReadOnlyList<SceneryPiece> pieces)
    {
        List<SceneryPiece> woods = [.. pieces.Where(p => p.Family == "woods")];
        if (woods.Count < 3)
            throw new InvalidOperationException($"the forest pool holds {I(woods.Count)} old woods borders: it needs at least 3 old woods");
        if (woods.FirstOrDefault(p => p.Kind != SceneryKind.Border || !p.Filler) is { } odd)
            throw new InvalidOperationException($"{odd.Name} is old woods but not a filler border: the pool needs at least 3 old woods, every one a filler");

        foreach (string family in s_borderFamilies)
        {
            if (!pieces.Any(p => p.Kind == SceneryKind.Border && p.Family == family))
                throw new InvalidOperationException($"the forest pool has no {family} piece: it needs a border of each family");
        }
        foreach (string family in s_singleCapFamilies)
        {
            if (!pieces.Any(p => p.Kind == SceneryKind.Cap && p.Family == family && p.Capped.Distinct().Count() == 1))
                throw new InvalidOperationException($"the forest pool has no {family} piece: it needs a cap of each family with exactly one capped side");
        }

        string ring = SceneryJsonWriter.WritePool(ChunkFiles.ForestMapId, pieces);
        try
        {
            SceneryFile.Read(ring, "the forest's ring file");
        }
        catch (InvalidDataException e)
        {
            throw new InvalidOperationException(e.Message, e);
        }
        return ring;
    }

    /// <summary>Refuses a scenery name that is also a chunk's, or that two scenery pieces share: the two share Maps/Decor.</summary>
    public static void CheckNames(IEnumerable<string> sceneryNames, IEnumerable<string> chunkNames)
    {
        HashSet<string> chunks = new(chunkNames, StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string name in sceneryNames)
        {
            if (chunks.Contains(name))
                throw new InvalidOperationException($"scenery piece {name} is also a chunk: the two would write one decor file");
            if (!seen.Add(name))
                throw new InvalidOperationException($"scenery piece name {name} is used twice");
        }
    }

    /// <summary>The item checked as a model of the piece's kit at any whole degree, and its turned box within the cell.</summary>
    private static Footprint CheckItem(SceneryPiece piece, DecorItem item, IReadOnlyDictionary<string, ModelBounds> kit)
    {
        item.Check(piece.Name, kit, DecorYaw.AnyDegree, piece.Kit);
        ModelBounds box = kit[item.Model];
        (float minX, float maxX, float minZ, float maxZ) = SceneryComposer.TurnedBox(item, box);

        var footprint = new Footprint(item, minX, maxX, minZ, maxZ, item.Y + box.MaxY * item.Scale);
        if (minX < -CellTolerance || minZ < -CellTolerance || maxX > ChunkPiece.CellSize + CellTolerance || maxZ > ChunkPiece.CellSize + CellTolerance)
        {
            throw new InvalidOperationException(
                $"{What(piece, item)} lies outside its cell: its box spans x {N(minX)} to {N(maxX)}, z {N(minZ)} to {N(maxZ)}, past 0-30 m");
        }
        return footprint;
    }

    /// <summary>
    /// An item drawn taller than <see cref="TallItem" /> standing within <see cref="EdgeBand" /> of one of the
    /// <paramref name="edges" /> is a kit tree at no more than the tree walls' scale (ForestDresser), never a twisted or
    /// ancient tree, or another model drawn no taller than <see cref="EdgeBandRockTop" />.
    /// </summary>
    private static void CheckEdgeBand(SceneryPiece piece, Footprint footprint, IReadOnlyList<Side> edges)
    {
        DecorItem item = footprint.Item;
        if (footprint.Top <= TallItem || !edges.Any(side => DistanceToEdge(side, item.X, item.Z) < EdgeBand)) return;

        string within = $"within {N(EdgeBand)} m of an edge";
        TreeSpecies? species = ForestDecorRules.SpeciesOf(item.Model);
        if (species == TreeSpecies.TwistedTree || item.Model.StartsWith(AncientTree, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{What(piece, item)} stands {within}: a tree that large would hang over the paths, so it stands further in");
        }
        if (species is { } tree)
        {
            float max = tree == TreeSpecies.DeadTree ? ForestDresser.WallDeadTreeMaxScale : ForestDresser.WallTreeMaxScale;
            if (item.Scale > max)
            {
                throw new InvalidOperationException(
                    $"{What(piece, item)} stands {within} at scale {N(item.Scale)}, above the tree walls' {N(max)}: its crown would hang over the paths");
            }
            return;
        }
        if (footprint.Top > EdgeBandRockTop)
        {
            throw new InvalidOperationException(
                $"{What(piece, item)} is drawn {N(footprint.Top)} m tall {within}, above {N(EdgeBandRockTop)} m: it would hang over the paths");
        }
    }

    /// <summary>Every ground vertex on the cell's edge at y 0, and every ground triangle touching it forest floor.</summary>
    private static void CheckForestGround(SceneryPiece piece)
    {
        foreach (Side side in s_sides)
        {
            foreach ((float x, float z) in EdgeVertices(side))
            {
                float y = piece.Terrain.Height(x, z);
                if (!(MathF.Abs(y) <= GroundTolerance))
                    throw new InvalidOperationException($"{piece.Name}: its ground at ({N(x)}, {N(z)}) meets its {side} edge at y {N(y)}, not 0");
            }
        }

        // The triangles SceneryObjWriter draws: each quad's two, wearing the material at their centroids. Only the
        // quads in the outer ring have a corner on the edge.
        int cells = (int)MathF.Round(ChunkPiece.CellSize / SceneryTerrain.Step);
        float s = SceneryTerrain.Step;
        for (int i = 0; i < cells; i++)
        {
            for (int j = 0; j < cells; j++)
            {
                if (i is not 0 && j is not 0 && i != cells - 1 && j != cells - 1) continue;
                float x0 = i * s, z0 = j * s;
                foreach ((float x, float z) in new[] { (x0 + s * 2f / 3f, z0 + s / 3f), (x0 + s / 3f, z0 + s * 2f / 3f) })
                {
                    string material = piece.Terrain.Material(x, z);
                    if (material != ForestFloor)
                    {
                        throw new InvalidOperationException(
                            $"{piece.Name}: its ground at ({N(x)}, {N(z)}), on its edge, wears '{material}', not {ForestFloor}: it would show a seam beside a chunk");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Each edge, in runs of <see cref="BarrierSpacing" />, has for every run a barrier item standing within
    /// <see cref="EdgeBand" /> of it whose turned box spans part of the run along the edge.
    /// </summary>
    private static void CheckBarrier(SceneryPiece piece, IReadOnlyList<Footprint> footprints)
    {
        foreach (Side side in s_sides)
        {
            for (int r = 0; r < BarrierRuns; r++)
            {
                float from = r * BarrierSpacing, to = from + BarrierSpacing;
                if (!footprints.Any(f => HoldsRun(f.Item, (f.MinX, f.MaxX, f.MinZ, f.MaxZ), side, from, to)))
                {
                    throw new InvalidOperationException(
                        $"{piece.Name}: no barrier along the {side} edge between {N(from)} and {N(to)} m: a view from the walkable edge would see through");
                }
            }
        }
    }

    /// <summary>The runs of <see cref="BarrierSpacing" /> metres each edge is checked in.</summary>
    internal static int BarrierRuns => (int)MathF.Round(ChunkPiece.CellSize / BarrierSpacing);

    /// <summary>
    /// Whether the item holds the run from <paramref name="from" /> to <paramref name="to" /> metres along the side's
    /// edge: a barrier model standing within <see cref="EdgeBand" /> of the edge whose turned box (piece-local) spans
    /// part of the run.
    /// </summary>
    internal static bool HoldsRun(DecorItem item, (float MinX, float MaxX, float MinZ, float MaxZ) box, Side side, float from, float to) =>
        IsBarrier(item.Model) && DistanceToEdge(side, item.X, item.Z) < EdgeBand
        && (side is Side.N or Side.S ? box.MaxX > from && box.MinX < to : box.MaxZ > from && box.MinZ < to);

    /// <summary>No tree, kit or ancient, within <see cref="CapClearing" /> of a capped edge's midpoint, in the piece's frame.</summary>
    private static void CheckCaps(SceneryPiece piece)
    {
        const float Half = ChunkPiece.CellSize / 2f;
        foreach (Side side in piece.Capped.Distinct())
        {
            (float mx, float mz) = side switch
            {
                Side.N => (Half, ChunkPiece.CellSize),
                Side.E => (ChunkPiece.CellSize, Half),
                Side.S => (Half, 0f),
                _ => (0f, Half),
            };
            DecorItem? tree = piece.Decor.FirstOrDefault(d =>
                (ForestDecorRules.SpeciesOf(d.Model) is not null || d.Model.StartsWith(AncientTree, StringComparison.Ordinal))
                && MathF.Sqrt((d.X - mx) * (d.X - mx) + (d.Z - mz) * (d.Z - mz)) < CapClearing);
            if (tree is not null)
            {
                throw new InvalidOperationException(
                    $"{What(piece, tree)} stands within {N(CapClearing)} m of its capped {side} edge's midpoint: the cap would not read from the camera");
            }
        }
    }

    /// <summary>
    /// Whether the model reads as impassable along an edge: a kit tree, undergrowth, a rock, a rim, a wall or a fence.
    /// </summary>
    internal static bool IsBarrier(string model) =>
        ForestDecorRules.SpeciesOf(model) is not null || s_barrierPrefixes.Any(p => model.StartsWith(p, StringComparison.Ordinal));

    /// <summary>Metres from a piece-local position to the side's edge.</summary>
    private static float DistanceToEdge(Side side, float x, float z) => side switch
    {
        Side.N => ChunkPiece.CellSize - z,
        Side.E => ChunkPiece.CellSize - x,
        Side.S => z,
        _ => x,
    };

    /// <summary>The ground grid's vertices on the side's edge (SceneryTerrain.Step apart), piece-local, corners included.</summary>
    private static IEnumerable<(float X, float Z)> EdgeVertices(Side side)
    {
        int cells = (int)MathF.Round(ChunkPiece.CellSize / SceneryTerrain.Step);
        for (int k = 0; k <= cells; k++)
        {
            float along = k * SceneryTerrain.Step;
            yield return side switch
            {
                Side.N => (along, ChunkPiece.CellSize),
                Side.E => (ChunkPiece.CellSize, along),
                Side.S => (along, 0f),
                _ => (0f, along),
            };
        }
    }

    private static (int X, int Z) Neighbour((int X, int Z) cell, Side side) => side switch
    {
        Side.N => (cell.X, cell.Z + 1),
        Side.E => (cell.X + 1, cell.Z),
        Side.S => (cell.X, cell.Z - 1),
        _ => (cell.X - 1, cell.Z),
    };

    /// <summary>Metres from an item's turned box in the piece to another cell (both on the world grid), 0 where they touch.</summary>
    private static float DistanceToCell(SceneryPiece piece, Footprint footprint, (int X, int Z) cell)
    {
        float ox = (piece.GridX - cell.X) * ChunkPiece.CellSize, oz = (piece.GridZ - cell.Z) * ChunkPiece.CellSize;
        float dx = Math.Max(0f, Math.Max(-(footprint.MaxX + ox), footprint.MinX + ox - ChunkPiece.CellSize));
        float dz = Math.Max(0f, Math.Max(-(footprint.MaxZ + oz), footprint.MinZ + oz - ChunkPiece.CellSize));
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static string TooTall(SceneryPiece piece, Footprint footprint, float top, float within, float distance) =>
        $"{What(piece, footprint.Item)} is drawn {N(footprint.Top)} m tall {N(distance)} m from the town: taller than {N(top)} m "
        + $"within {N(within)} m of the wall, it would stand between the camera and the player";

    private static string What(SceneryPiece piece, DecorItem item) => $"{piece.Name}: {item.Model} at ({N(item.X)}, {N(item.Z)})";

    private static string N(float value) => ForestText.N(value);

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>An item's turned and scaled kit box on x and z, piece-local, and the height it is drawn to.</summary>
    private readonly record struct Footprint(DecorItem Item, float MinX, float MaxX, float MinZ, float MaxZ, float Top);
}
