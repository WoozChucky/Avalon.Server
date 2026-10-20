using Avalon.World.ChunkLayouts;
using static Avalon.ChunkGen.SceneryShapes;
using Outline = System.Collections.Generic.IReadOnlyList<(float X, float Z)>;

namespace Avalon.ChunkGen;

/// <summary>
/// The forest's scenery pieces (map 2, scenery ring #1110): what the ring generator draws on the cells around a
/// generated layout. Every piece is drawn from the forest kit (the darkened nature kit and the custom avalon_* forest
/// models), meets every edge at y 0 on forest floor, carries a barrier band on all four sides (SceneryDresser) and
/// keeps its feature inside the band, so any piece sits beside any other or any chunk at any rotation. A border's front
/// is S: the ring generator turns it toward the layout. The borders:
/// <list type="bullet">
/// <item>old woods (the filler, four variants): two or three ancient trees, or one beside a glade among kit trees, a
/// darker understorey, mossy stones, fallen wood, gentle relief;</item>
/// <item>mossy cliffs (two): a plateau inside the band, its rough rock face dressed with layered face sections, ledges,
/// roots and creepers over the lip, scree at the foot, trees on top;</item>
/// <item>ravine into mist (two): an irregular cut down to <see cref="RavineFloor" />, rock walls, an overhanging lip
/// all round its brink, dead trees rising out of three mist layers;</item>
/// <item>giant fallen tree (one): the giant lying across the cell, its root plate on edge at its root end over the torn
/// earth it left;</item>
/// <item>dark stream (two): water rising among mossy stones and running down its bed into a still pool or away among
/// stones, stones and pebbles along its banks.</item>
/// </list>
/// The exit caps end every open exit's path: on each capped side the path carries on from the edge's midpoint as a dirt
/// stub and ends under an obstacle covering the whole edge band (a rockfall, a giant trunk across it, a thicket, or a
/// ravine's brink where it breaks off), with no tree within 10 m of the midpoint; one cap of each obstacle with its S
/// side capped, and caps for two adjacent, two opposite, three and four capped sides.
/// The town-ward pieces fill the eight spots round the entry chunk, never turned: one scene in world metres cut into a
/// piece per spot (ForestScenery.TownWard.cs), the road from the town ending at a low wall behind the portal, field
/// edges either side of it, the woods' fringe with deadfall and thorn toward the entry's corners.
/// Every piece is composed by hand around a few fixed points and dressed from its own random stream (its name's seed),
/// so a rerun writes the same files.
/// </summary>
public static partial class ForestScenery
{
    /// <summary>The ravine's floor, below the forest's mist floor (-10 m).</summary>
    private const float RavineFloor = -15f;

    /// <summary>The mist layers in a ravine, inset <see cref="MistInset" /> from its walls.</summary>
    private static readonly float[] s_mistLayers = [-8.5f, -6.5f, -4.5f];

    private const float MistInset = 0.4f;

    /// <summary>A stream's bed lies this far down under its middle.</summary>
    private const float StreamBed = 0.8f;

    /// <summary>The most the old woods' ground rises or falls inside the band, in metres.</summary>
    private const float ReliefHeight = 0.45f;

    private const float Cell = ChunkPiece.CellSize;

    private const string Floor = "forest_floor", Rock = "cliff_rock";

    private static readonly Side[] s_sides = [Side.N, Side.E, Side.S, Side.W];

    /// <summary>
    /// The ravine lip sections and the length each was built to along its edge at scale 1 (not its box's: a brink juts
    /// past its ends), so they must change when a model does.
    /// </summary>
    private static readonly (string Model, float Length)[] s_lips = [("avalon_ravine_lip_1", 6f), ("avalon_ravine_lip_2", 5.2f), ("avalon_ravine_lip_3", 6.8f)];

    /// <summary>How far two face or lip sections overlap end to end, in metres.</summary>
    private const float SectionOverlap = 0.9f;

    private static readonly string[] s_scree = ["Pebble_Round_1", "Pebble_Round_3", "Pebble_Round_5", "Pebble_Round_2"];

    private static readonly string[] s_mossyRocks = ["avalon_rock_mossy_1", "avalon_rock_mossy_2", "avalon_rock_mossy_3"];

    /// <summary>
    /// The largest a kit rock (<c>Rock_Medium_*</c>) is drawn among scree and torn earth: larger, its pale faces read as
    /// dice beside the mossy stones.
    /// </summary>
    private const float KitScreeMaxScale = 0.26f;

    /// <summary>Every forest scenery piece: the eleven borders, the eight exit caps and the eight town-ward pieces.</summary>
    public static IReadOnlyList<SceneryPiece> Pieces()
    {
        IReadOnlyDictionary<string, ModelBounds> kit = KitBounds.Load();
        return
        [
            Woods(1, kit, TreeMix.Old, [(1, 9.5f, 10.5f), (4, 20.5f, 12f), (3, 13.5f, 20.5f)], WoodsLogAndStones),
            Woods(2, kit, TreeMix.Mixed, [(2, 10.5f, 18.5f), (3, 19.5f, 10f)], WoodsOutcrop),
            Woods(3, kit, TreeMix.Pine, [(4, 10f, 9.5f), (1, 20.5f, 19f), (2, 10.5f, 20.5f)], WoodsDeadfall),
            // One ancient off the centre beside a glade, kit trees round it: the filler that reads apart at a glance.
            Woods(4, kit, TreeMix.Broadleaf, [(4, 20.5f, 20.5f)], WoodsGlade, glade: (12.5f, 12f, 5.5f)),
            // A long low front tier along the S edge, a higher back tier stepping up behind it.
            Cliffs(1, kit, TreeMix.Mixed,
                [new CliffTier([(6.4f, 7f), (23.8f, 6.4f), (23.2f, 14.6f), (6.8f, 15.2f)], 6f),
                 new CliffTier([(6.6f, 12f), (17.5f, 11f), (22.5f, 16f), (16.5f, 23.4f), (8f, 22f)], 7.8f)],
                ancientOnTop: true),
            // A crag with a long S face whose top falls back to the ground on the N as an earth bank.
            Cliffs(2, kit, TreeMix.Pine,
                [new CliffTier([(6.3f, 8.5f), (14f, 6.4f), (23.6f, 7.6f), (22.8f, 16.8f), (12.5f, 17.8f), (6.6f, 15.5f)], 7.4f, Ramp: 3)],
                ancientOnTop: false),
            // A broad pit: a bay to the north-east, a spur of ground jutting in from the west, a pinch on the east.
            Ravine(1, kit, TreeMix.Old,
                [(11.5f, 24.8f), (19f, 25.2f), (23f, 22f), (25f, 17f), (21.4f, 14.4f), (23.6f, 9.6f), (19.8f, 5.4f), (11.2f, 6.2f),
                 (7.4f, 9f), (10.2f, 12.8f), (9.4f, 15.6f), (5.2f, 17.8f), (6f, 22.4f)],
                [("DeadTree_5", 15.5f, 17.5f, 1.15f), ("DeadTree_2", 17f, 10f, 1.5f)]),
            // A ragged slot running corner to corner, 4-8 m wide, both ends in the band.
            Ravine(2, kit, TreeMix.Mixed,
                [(5.6f, 11.5f), (7.6f, 14.6f), (9.2f, 15.4f), (11.6f, 17.6f), (14f, 17.8f), (15.8f, 20.6f), (18.6f, 21.4f), (20.2f, 23.6f),
                 (23f, 24.2f), (24.6f, 21f), (23.6f, 17.8f), (21.2f, 16.8f), (19.6f, 14.2f), (17.4f, 13.6f), (16.2f, 11f), (13.2f, 10.4f),
                 (11.6f, 8f), (9f, 6f), (6f, 7.2f)],
                [("DeadTree_5", 11.5f, 12.8f, 1.05f), ("DeadTree_2", 18f, 17.8f, 1.4f)]),
            Giant(kit),
            Stream(1, kit, TreeMix.Broadleaf,
                [(22.3f, 17f), (19.6f, 18f), (17f, 16.6f), (15.6f, 14f), (13.4f, 12.6f), (11.2f, 13f)], pool: (9.4f, 13.6f, 2.3f),
                ancient: (2, 14.5f, 22.5f)),
            Stream(2, kit, TreeMix.Mixed,
                [(21.4f, 21f), (18.9f, 21.8f), (16.2f, 21.2f), (13.9f, 19.4f), (12.7f, 16.6f), (12.8f, 13.6f), (11.7f, 11.1f), (10f, 9.5f)], pool: null,
                ancient: null),
            // The exit caps: each capped side's path ends under its obstacle, every other side a band.
            Cap("forest_cap_rockfall_01", "rockfall", kit, TreeMix.Old, [new(Side.S, Obstacle.Rockfall)], [(1, 8.6f, 21.4f), (3, 21.8f, 22.6f)], 0),
            Cap("forest_cap_trunk_01", "trunk", kit, TreeMix.Mixed, [new(Side.S, Obstacle.Trunk)], [(2, 18.5f, 21.8f)], 0),
            Cap("forest_cap_thicket_01", "thicket", kit, TreeMix.Pine, [new(Side.S, Obstacle.Thicket)], [], 4),
            Cap("forest_cap_brink_01", "brink", kit, TreeMix.Old,
                [new(Side.S, Obstacle.Brink, s_brinkRavine, [("DeadTree_5", 15.5f, 12.5f, 1.1f), ("DeadTree_2", 19.8f, 9.6f, 1.45f)])], [], 0),
            Cap("forest_cap_corner_01", "cap", kit, TreeMix.Mixed, [new(Side.S, Obstacle.Rockfall), new(Side.W, Obstacle.Thicket)], [(4, 21f, 21f)], 2),
            Cap("forest_cap_opposite_01", "cap", kit, TreeMix.Broadleaf, [new(Side.S, Obstacle.Trunk), new(Side.N, Obstacle.Rockfall)],
                [(1, 6.5f, 15f), (3, 23.6f, 15f)], 0),
            Cap("forest_cap_three_01", "cap", kit, TreeMix.Old,
                [new(Side.S, Obstacle.Rockfall), new(Side.N, Obstacle.Trunk), new(Side.W, Obstacle.Thicket)], [(2, 21.5f, 15f)], 1),
            // Ancient trees only in the centre, 15 m from every midpoint.
            Cap("forest_cap_four_01", "cap", kit, TreeMix.Mixed,
                [new(Side.S, Obstacle.Rockfall), new(Side.N, Obstacle.Trunk), new(Side.E, Obstacle.Brink, s_capGorge), new(Side.W, Obstacle.Thicket)],
                [(4, 15f, 15f)], 0),
            // The town-ward scene round the entry chunk, one piece per spot.
            .. TownWard(kit),
        ];
    }

    // ---- old woods ----

    /// <summary>
    /// An old-woods border: its ancient trees, its feature, the band, then the floor. With a <paramref name="glade" />
    /// (centre and radius) the floor inside it stays open (ground cover only) and no tree stands in it or between it
    /// and the camera; kit trees stand round it instead.
    /// </summary>
    private static SceneryPiece Woods(
        int n, IReadOnlyDictionary<string, ModelBounds> kit, TreeMix mix, (int Variant, float X, float Z)[] ancients, Action<SceneryDresser> feature,
        (float X, float Z, float Radius)? glade = null)
    {
        string name = $"forest_ring_woods_{n:00}";
        int seed = ForestDresser.StableSeed(name);
        var terrain = new SceneryTerrain((x, z) => Relief(seed, x, z), (_, _) => Floor);
        var d = new SceneryDresser(name, terrain, kit);
        foreach ((int variant, float x, float z) in ancients)
        {
            if (!d.TryAncient(variant, x, z, d.Rng.Next(360)))
                throw new InvalidOperationException($"{name}: ancient tree {variant} does not fit at ({ForestText.N(x)}, {ForestText.N(z)})");
        }
        feature(d);
        // No band tree between an ancient trunk and the S edge (the layout, at rotation 0) or the game camera.
        bool Trunk(float x, float z, float reach) => ancients.Any(t => SceneryDresser.Distance(x, z, t.X, t.Z) < reach);
        bool Open(float x, float z, float margin) => glade is { } g && SceneryDresser.Distance(x, z, g.X, g.Z) < g.Radius + margin;
        bool Hides(float x, float z) =>
            ancients.Any(t => SegmentDistance(x, z, (t.X, t.Z), (t.X, 0f)) < 2.2f) || InView(x, z, (px, pz) => Trunk(px, pz, 3f) || Open(px, pz, 0f));
        d.BarrierBand(mix, s_sides, keepTreesClear: Hides);
        Inner(d, mix, saplings: glade is null ? 1 : 5, understorey: glade is null ? 80 : 60, cover: 60, rocks: 5, clear: (x, z) => Open(x, z, 1.2f),
            treesClear: Hides);
        return Border(name, "woods", 3f, terrain, d, filler: true);
    }

    /// <summary>
    /// A long fallen log across the middle, mushrooms on it, mossy stones in a cluster, a bramble patch and a boulder
    /// under roots and a creeper.
    /// </summary>
    private static void WoodsLogAndStones(SceneryDresser d)
    {
        Log(d, "avalon_log_fallen_1", 15.5f, 15f, 68, 1.35f);
        Log(d, "avalon_log_fallen_2", 19.2f, 22.4f, 152, 1.1f);
        d.Cluster(21.5f, 20.5f, ["avalon_rock_mossy_1", "avalon_rock_mossy_2", "avalon_rock_mossy_3", "avalon_rock_mossy_2"], 2.6f, 0.8f, 1.25f);
        d.Cluster(8f, 17f, ["avalon_bramble_2", "avalon_bramble_1", "avalon_bramble_1"], 2.2f, 0.9f, 1.15f);
        // A mossy boulder by the stones, roots gripping over it and a creeper down its far side.
        if (!d.TryPlace("avalon_boulder_2", 18.6f, 19f, 1.3f, 1f, 0.15f, yaw: 30))
            throw new InvalidOperationException($"{d.Name}: the boulder does not fit at (18.6, 19)");
        HangOver(d, "avalon_roots_hanging_1", 18.6f, 19f, 0.92f, 210, top: 1.5f - 0.25f);
        HangOver(d, "avalon_creeper_1", 18.6f, 19f, 0.92f, 30, top: 1.5f - 0.3f);
    }

    /// <summary>
    /// A mossy boulder outcrop with roots gripping over its top and a creeper down its face, and a dead snag.
    /// </summary>
    private static void WoodsOutcrop(SceneryDresser d)
    {
        Outcrop(d, 20f, 20.5f);
        d.TryTree(TreeSpecies.DeadTree, 9f, 9.5f, 0.95f);
        Log(d, "avalon_log_fallen_2", 15.5f, 8.5f, 20, 1.2f);
        d.Cluster(15f, 15f, ["avalon_rock_mossy_3", "avalon_rock_mossy_1"], 1.6f, 0.8f, 1.1f);
    }

    /// <summary>
    /// Two fallen logs crossed, a line of mossy stones, a boulder under roots and a creeper, a dead snag among the
    /// ferns.
    /// </summary>
    private static void WoodsDeadfall(SceneryDresser d)
    {
        Log(d, "avalon_log_fallen_1", 19.5f, 9f, 115, 1.25f);
        Log(d, "avalon_log_fallen_2", 21f, 10.5f, 35, 1.1f);
        for (int i = 0; i < 4; i++)
            d.TryPlace(d.Pick(["avalon_rock_mossy_1", "avalon_rock_mossy_2", "avalon_rock_mossy_3"]), 14f + i * 1.9f, 14.5f - i * 0.5f + d.Uniform(-0.4f, 0.4f), 0.8f, d.Uniform(0.75f, 1.2f), 0.05f);
        d.TryTree(TreeSpecies.DeadTree, 15.5f, 21.5f, 1.05f);
        // A split boulder by the logs, roots gripping over it and a creeper down its face.
        if (!d.TryPlace("avalon_boulder_3", 22.4f, 15.2f, 1.9f, 0.9f, 0.2f, yaw: 250))
            throw new InvalidOperationException($"{d.Name}: the boulder does not fit at (22.4, 15.2)");
        HangOver(d, "avalon_roots_hanging_1", 22.4f, 15.2f, 1.46f * 0.9f, 70, top: 2.41f * 0.9f - 0.3f);
        HangOver(d, "avalon_creeper_1", 22.4f, 15.2f, 1.46f * 0.9f, 250, top: 2.41f * 0.9f - 0.3f);
    }

    /// <summary>
    /// The glade beside a single ancient tree (woods_04): a log lying across its edge, ferns ringing it, short grass,
    /// clover and a few flowers where light reaches the floor, mossy stones; a boulder under a creeper and a thicket of
    /// brambles and dark hedges in the woods round it.
    /// </summary>
    private static void WoodsGlade(SceneryDresser d)
    {
        const float GX = 12.5f, GZ = 12f, Radius = 5.5f;
        Log(d, "avalon_log_fallen_1", 14.2f, 8.4f, 160, 1.2f);
        d.Cluster(10.2f, 14.6f, ["avalon_rock_mossy_2", "avalon_rock_mossy_1", "avalon_rock_mossy_3"], 1.3f, 0.7f, 1.05f);
        float Rim(float x, float z) => SceneryDresser.Distance(x, z, GX, GZ) - Radius;
        d.Scatter([("Fern_1", 6), ("Plant_1_Big", 1), ("Grass_Wispy_Tall", 2)], 22, 0.85f, 1.3f, (x, z) => Rim(x, z) is > -1.4f and < 0.6f);
        d.Scatter([("Grass_Common_Short", 6), ("Grass_Wispy_Short", 5), ("Clover_1", 3), ("Clover_2", 3), ("Flower_3_Group", 1), ("Flower_4_Single", 1),
            ("Plant_7", 2)], 34, 0.7f, 1.05f, (x, z) => Rim(x, z) < -1.2f, spread: 0.35f);
        if (!d.TryPlace("avalon_boulder_3", 21f, 9.5f, 2f, 1f, 0.2f, yaw: 205))
            throw new InvalidOperationException($"{d.Name}: the boulder does not fit at (21, 9.5)");
        HangOver(d, "avalon_creeper_1", 21f, 9.5f, 1.46f, 205, top: 2.41f - 0.3f);
        d.Cluster(9.5f, 21.5f, ["avalon_bramble_2", "avalon_hedge_dark", "avalon_bramble_1", "avalon_hedge_dark", "avalon_bramble_2"], 2.8f, 0.9f, 1.25f);
    }

    // ---- ravine into mist ----

    private static SceneryPiece Ravine(
        int n, IReadOnlyDictionary<string, ModelBounds> kit, TreeMix mix, Outline brink, (string Model, float X, float Z, float Scale)[] dead)
    {
        string name = $"forest_ring_ravine_{n:00}";
        int seed = ForestDresser.StableSeed(name);
        if (SignedArea(brink) >= 0f) throw new InvalidOperationException($"{name}: the brink must run clockwise, so its wall looks into the ravine");

        float Height(float x, float z)
        {
            float edge = OutlineDistance(brink, x, z);
            if (Inside(brink, x, z)) return RavineFloor * MathF.Min(1f, edge);
            return Relief(seed, x, z) * Smooth(2f, 5f, edge);
        }

        // A quad straddling the brink falls from the rim into the ravine: rock, as its wall is.
        var terrain = new SceneryTerrain(Height, (x, z) => Height(x, z) < -0.3f || Steep(Height, x, z) ? Rock : Floor)
        {
            Walls = [RavineWall(brink)],
            Surfaces = MistLayers(brink),
        };
        var d = new SceneryDresser(name, terrain, kit);

        Lips(d, brink);

        foreach ((string model, float x, float z, float scale) in dead)
        {
            if (!d.TryAdd(model, x, z, d.Rng.Next(360), scale, sink: 0.3f))
                throw new InvalidOperationException($"{name}: dead tree {model} does not fit at ({ForestText.N(x)}, {ForestText.N(z)})");
        }

        bool Ground(float x, float z, float keep) => !Inside(brink, x, z) && OutlineDistance(brink, x, z) >= keep;
        // No band tree beside the brink or between the ravine and the game camera.
        d.BarrierBand(mix, s_sides, (x, z) => !Ground(x, z, 1.8f), (x, z) => !Ground(x, z, 4.5f) || InView(x, z, (px, pz) => !Ground(px, pz, 1f)));
        // Along the brink: ferns, grass and the odd bush; then the floor between it and the band.
        d.Scatter([("Fern_1", 5), ("Grass_Wispy_Tall", 4), ("Plant_1", 2), ("avalon_hedge_dark", 1), ("Grass_Common_Tall", 2)], 34, 0.8f, 1.2f,
            (x, z) => Ground(x, z, 1.5f) && OutlineDistance(brink, x, z) < 3.5f);
        d.Understorey(24, (x, z) => Ground(x, z, 2.5f) && EdgeDistance(x, z) > 3f);
        d.GroundCover(30, (x, z) => Ground(x, z, 1.4f));
        return Border(name, "ravine", 1f, terrain, d);
    }

    /// <summary>The ravine's rough rock wall all round its brink (clockwise), down past its floor.</summary>
    private static SceneryWall RavineWall(Outline brink) => new(brink, Closed: true, Bottom: RavineFloor - 0.5f, Top: 0f, Rock);

    /// <summary>The ravine's mist layers, inset <see cref="MistInset" /> from its walls.</summary>
    private static ScenerySurface[] MistLayers(Outline brink) =>
        [.. s_mistLayers.Select(y => ScenerySurface.Area((x, z) => Inside(brink, x, z) && OutlineDistance(brink, x, z) >= MistInset, y, "mist"))];

    /// <summary>
    /// The lip all round a ravine's brink (clockwise), its void toward the ravine: one section a side, two on a long side
    /// at different scales, each set a little out over the drop and turned a few degrees, with a few more roots and
    /// creepers under it. A section whose turned box would cross the cell's edge (a brink near it) is laid square and,
    /// if it must, smaller, down to 0.8.
    /// </summary>
    private static void Lips(SceneryDresser d, Outline brink)
    {
        int last = -1;
        for (int i = 0; i < brink.Count; i++)
        {
            (float X, float Z) a = brink[i], b = brink[(i + 1) % brink.Count];
            float length = SceneryDresser.Distance(a.X, a.Z, b.X, b.Z);
            (float tx, float tz) = ((b.X - a.X) / length, (b.Z - a.Z) / length);
            (float nx, float nz) = (tz, -tx);                    // into the ravine
            int yaw = SceneryDresser.YawToward(nx, nz);
            float need = length + SectionOverlap;
            List<(int Lip, float Scale)> chain = [];
            if (need <= s_lips.Max(l => l.Length) * 1.1f)
            {
                int lip = Enumerable.Range(0, s_lips.Length).Where(l => l != last)
                    .OrderBy(l => MathF.Abs(need / s_lips[l].Length - 1f) + d.Uniform(0f, 0.08f)).First();
                chain.Add((lip, Math.Clamp(need / s_lips[lip].Length, 0.8f, 1.25f)));
            }
            else
            {
                int first = Enumerable.Range(0, s_lips.Length).Where(l => l != last).OrderBy(_ => d.Rng.Next()).First();
                int second = Enumerable.Range(0, s_lips.Length).Where(l => l != first).OrderBy(_ => d.Rng.Next()).First();
                float firstScale = d.Uniform(0.85f, 1.15f);
                float secondScale = Math.Clamp((need + SectionOverlap - s_lips[first].Length * firstScale) / s_lips[second].Length, 0.8f, 1.25f);
                chain.Add((first, firstScale));
                chain.Add((second, secondScale));
            }
            last = chain[^1].Lip;
            float total = chain.Sum(c => s_lips[c.Lip].Length * c.Scale) - SectionOverlap * (chain.Count - 1);
            float cursor = (length - total) / 2f;
            foreach ((int lip, float scale) in chain)
            {
                float along = cursor + s_lips[lip].Length * scale / 2f;
                cursor += s_lips[lip].Length * scale - SectionOverlap;
                // Out over the drop only: set back, its rock would stand behind the wall.
                float over = d.Uniform(0f, 0.18f);
                float mx = a.X + tx * along + nx * over, mz = a.Z + tz * along + nz * over;
                bool laid = d.TryAddAt(s_lips[lip].Model, mx, 0f, mz, yaw + d.Rng.Next(-5, 6), scale);
                for (float smaller = scale; !laid && smaller >= 0.8f - 0.001f; smaller -= 0.05f)
                    laid = d.TryAddAt(s_lips[lip].Model, mx, 0f, mz, yaw, smaller);
                if (!laid)
                    throw new InvalidOperationException($"{d.Name}: lip {s_lips[lip].Model} at ({ForestText.N(mx)}, {ForestText.N(mz)}) crosses the cell's edge");
                d.Take(mx - nx * 0.6f, mz - nz * 0.6f, 1.4f);
            }
            if (d.Rng.NextDouble() < 0.45)
            {
                float along = d.Uniform(0.2f, 0.8f) * length;
                d.TryAddAt(d.Rng.NextDouble() < 0.6 ? "avalon_roots_hanging_2" : "avalon_creeper_2",
                    a.X + tx * along + nx * 0.8f, 0f, a.Z + tz * along + nz * 0.8f, yaw + d.Rng.Next(-12, 13), d.Uniform(0.9f, 1.2f));
            }
        }
    }

    // ---- giant fallen tree ----

    private static SceneryPiece Giant(IReadOnlyDictionary<string, ModelBounds> kit)
    {
        const string Name = "forest_ring_giant_01", Model = "avalon_giant_fallen_1";
        // Its +X, root end to crown end, runs west, 10 degrees off the x axis: along the front, across the view from the
        // layout, so the whole trunk and the plate's face show.
        const int GiantYaw = 170;
        const float HalfLength = 9.1f, PlateBack = 0.25f, PlateFromCentre = 6.8f;
        int seed = ForestDresser.StableSeed(Name);
        (float dx, float dz) = Yaw.Turn(1f, 0f, GiantYaw);
        // The plate stands 6.8 m east of the cell's centre (the 5.6 m plate outside the band), the giant running back
        // across the centre; its broken crown end, at most 3.8 m tall, reaches into the west band.
        float px = 15f - dx * PlateFromCentre, pz = 15f - dz * PlateFromCentre;
        float gx = px + dx * (HalfLength + PlateBack), gz = pz + dz * (HalfLength + PlateBack);
        // The torn earth: the hole the roots left, under the plate's torn side.
        float hx = px - dx * 2.1f, hz = pz - dz * 2.1f;
        (float X, float Z) root = (gx - dx * HalfLength, gz - dz * HalfLength), crown = (gx + dx * HalfLength, gz + dz * HalfLength);

        float Trunk(float x, float z) => SegmentDistance(x, z, root, crown);
        float Hole(float x, float z) => -0.65f * (1f - Smooth(1.2f, 3.1f, SceneryDresser.Distance(x, z, hx, hz)));
        float Height(float x, float z) => Relief(seed, x, z) * Smooth(2.5f, 5f, Trunk(x, z)) * Smooth(3f, 5f, SceneryDresser.Distance(x, z, hx, hz)) + Hole(x, z);
        var terrain = new SceneryTerrain(Height, (x, z) => Hole(x, z) < -0.12f ? "dirt_road" : Floor);
        var d = new SceneryDresser(Name, terrain, kit);

        if (!d.TryAddAt(Model, gx, 0f, gz, GiantYaw, 1f) || !d.TryAddAt("avalon_root_plate_1", px, Height(px, pz), pz, GiantYaw, 1f))
            throw new InvalidOperationException($"{Name}: the giant or its root plate crosses the cell's edge");
        for (float t = -HalfLength; t <= HalfLength; t += 2f) d.Take(gx + dx * t, gz + dz * t, 1.6f);
        d.Take(px, pz, 2.4f);

        // The torn earth: stones and pebbles thrown out of the hole, a few pale roots' worth of fern at its rim.
        for (int i = 0; i < 9; i++)
        {
            float a = d.Uniform(0f, 360f), r = d.Uniform(0.4f, 2.6f);
            (float s, float c) = Yaw.Of((int)a);
            string model = i < 6 ? d.Pick(s_scree) : d.Pick(["avalon_rock_mossy_3", "Rock_Medium_2"]);
            float scale = i < 6 ? d.Uniform(1.4f, 2.6f) : model == "Rock_Medium_2" ? d.Uniform(0.16f, KitScreeMaxScale) : d.Uniform(0.45f, 0.65f);
            d.TryPlace(model, hx + c * r, hz + s * r, 0.3f, scale, 0.03f);
        }

        // Ferns and big plants crowding both flanks of the trunk, mushrooms along it.
        d.Scatter([("Fern_1", 6), ("Plant_1_Big", 2), ("Grass_Wispy_Tall", 2), ("Mushroom_Laetiporus", 1)], 36, 0.85f, 1.35f,
            (x, z) => Trunk(x, z) is > 1.9f and < 4.6f);
        if (!d.TryAncient(3, 16.5f, 23.2f, d.Rng.Next(360)))
            throw new InvalidOperationException($"{Name}: the ancient tree does not fit");
        d.TryTree(TreeSpecies.DeadTree, 23f, 22.5f, 0.9f);
        d.TryTree(TreeSpecies.Pine, 7.6f, 20.4f, 0.95f);
        Log(d, "avalon_log_fallen_2", 22f, 9.6f, 80, 0.9f);

        // No band tree on the giant or its plate, nor between them and the game camera.
        bool OnGiant(float x, float z) => Trunk(x, z) < 3.5f || SceneryDresser.Distance(x, z, px, pz) < 4f;
        d.BarrierBand(TreeMix.Mixed, s_sides, (x, z) => Trunk(x, z) < 3.2f || SceneryDresser.Distance(x, z, px, pz) < 4.5f, (x, z) => InView(x, z, OnGiant));
        d.Understorey(40, (x, z) => Trunk(x, z) > 2.6f && EdgeDistance(x, z) > 3f && SceneryDresser.Distance(x, z, hx, hz) > 2.8f);
        d.GroundCover(45, (x, z) => Trunk(x, z) > 2f);
        d.MossyRocks(3, (x, z) => Trunk(x, z) > 4f && EdgeDistance(x, z) > 6f);
        return Border(Name, "giant", 0.75f, terrain, d);
    }

    // ---- dark stream ----

    private static SceneryPiece Stream(
        int n, IReadOnlyDictionary<string, ModelBounds> kit, TreeMix mix, Outline course, (float X, float Z, float Radius)? pool,
        (int Variant, float X, float Z)? ancient)
    {
        string name = $"forest_ring_stream_{n:00}";
        int seed = ForestDresser.StableSeed(name);
        const float HalfWidth = 1.4f, Top = -0.4f, Slope = 0.012f;

        // The water's level at each course point: it runs downhill from the spring.
        List<(float X, float Y, float Z)> water = [];
        float run = 0f;
        for (int i = 0; i < course.Count; i++)
        {
            if (i > 0) run += SceneryDresser.Distance(course[i - 1].X, course[i - 1].Z, course[i].X, course[i].Z);
            water.Add((course[i].X, Top - Slope * run, course[i].Z));
        }
        float poolY = water[^1].Y - 0.01f;

        float Bed(float x, float z) => PolylineDistance(course, x, z);
        float Basin(float x, float z) =>
            pool is { } p ? -(StreamBed + 0.2f) * (1f - Smooth(p.Radius * 0.55f, p.Radius + 0.9f, SceneryDresser.Distance(x, z, p.X, p.Z))) : 0f;
        float Height(float x, float z)
        {
            float bed = -StreamBed * (1f - Smooth(0.3f * HalfWidth, HalfWidth + 0.7f, Bed(x, z)));
            return MathF.Min(bed, Basin(x, z)) + Relief(seed, x, z) * Smooth(HalfWidth + 1.5f, HalfWidth + 4f, MathF.Min(Bed(x, z), PoolDistance(x, z)));
        }
        float PoolDistance(float x, float z) => pool is { } p ? SceneryDresser.Distance(x, z, p.X, p.Z) - p.Radius : float.MaxValue;
        bool Wet(float x, float z) => Bed(x, z) < HalfWidth + 0.5f || PoolDistance(x, z) < 0.6f;

        List<ScenerySurface> surfaces = [ScenerySurface.Strip(water, HalfWidth, "water")];
        if (pool is { } still)
            surfaces.Add(ScenerySurface.Area((x, z) => SceneryDresser.Distance(x, z, still.X, still.Z) < still.Radius, poolY, "water"));
        var terrain = new SceneryTerrain(Height, (_, _) => Floor) { Surfaces = surfaces };
        var d = new SceneryDresser(name, terrain, kit);
        if (ancient is { } tree && !d.TryAncient(tree.Variant, tree.X, tree.Z, d.Rng.Next(360)))
            throw new InvalidOperationException($"{name}: the ancient tree does not fit");

        // The spring: mossy stones and a boulder round the water's first metre.
        (float sx, float sz) = course[0];
        (float bx, float bz) = (course[0].X - (course[1].X - course[0].X) * 0.35f, course[0].Z - (course[1].Z - course[0].Z) * 0.35f);
        d.TryPlace("avalon_boulder_2", bx, bz, 1.2f, 0.85f, 0.2f);
        d.Cluster(sx, sz, ["avalon_rock_mossy_2", "avalon_rock_mossy_1", "avalon_rock_mossy_3", "avalon_rock_mossy_1"], 1.9f, 0.6f, 1f, keep: 0.7f);

        // Its end: a still pool ringed with stones, or the water sinking away among stones and mossy rocks.
        if (pool is { } end)
        {
            for (int i = 0; i < 16; i++)
            {
                (float s, float c) = Yaw.Of(i * 360 / 16 + d.Rng.Next(-8, 9));
                float r = end.Radius + d.Uniform(0.1f, 0.6f);
                bool pebble = i % 3 == 1;
                string model = pebble ? d.Pick(s_scree) : d.Pick(s_mossyRocks);
                float scale = pebble ? d.Uniform(1.2f, 2f) : d.Uniform(0.35f, 0.75f);
                if (Bed(end.X + c * r, end.Z + s * r) > HalfWidth + 0.3f) d.TryPlace(model, end.X + c * r, end.Z + s * r, 0.25f, scale, 0.05f);
            }
        }
        else
        {
            (float ex, float ez) = course[^1];
            d.TryPlace("avalon_boulder_1", ex, ez, 1f, 0.9f, 0.25f);
            d.Cluster(ex, ez, ["avalon_rock_mossy_2", "avalon_rock_mossy_3", "Rock_Medium_1", "avalon_rock_mossy_1"], 2f, 0.55f, 0.95f, keep: 0.6f);
            for (int i = 0; i < 10; i++)
                d.TryPlace(d.Pick(s_scree), ex + d.Uniform(-2.2f, 2.2f), ez + d.Uniform(-2.2f, 2.2f), 0.25f, d.Uniform(1.4f, 2.6f), 0.03f);
        }

        // Stones breaking the water, and stones and pebbles along both banks.
        for (int i = 1; i < water.Count - 1; i++)
        {
            if (d.Rng.NextDouble() >= 0.55) continue;
            d.TryAddAt(d.Pick(s_mossyRocks), water[i].X + d.Uniform(-0.35f, 0.35f), water[i].Y - 0.12f,
                water[i].Z + d.Uniform(-0.35f, 0.35f), d.Rng.Next(360), d.Uniform(0.28f, 0.42f));
        }
        for (int i = 0; i < 70; i++)
        {
            int segment = d.Rng.Next(course.Count - 1);
            float t = d.Uniform(0f, 1f), side = d.Rng.Next(2) == 0 ? -1f : 1f;
            (float X, float Z) a = course[segment], b = course[segment + 1];
            float length = SceneryDresser.Distance(a.X, a.Z, b.X, b.Z);
            float off = side * d.Uniform(HalfWidth * 0.85f, HalfWidth + 0.9f);
            float x = a.X + (b.X - a.X) * t + (b.Z - a.Z) / length * off, z = a.Z + (b.Z - a.Z) * t - (b.X - a.X) / length * off;
            bool pebble = d.Rng.NextDouble() < 0.35;
            string model = pebble ? d.Pick(s_scree) : d.Pick(s_mossyRocks);
            float scale = pebble ? d.Uniform(1f, 1.9f) : d.Uniform(0.3f, 0.65f);
            d.TryPlace(model, x, z, pebble ? 0.2f : 0.55f * scale, scale, 0.06f);
        }
        d.Scatter([("Fern_1", 5), ("Grass_Common_Tall", 3), ("Grass_Wispy_Tall", 3), ("Plant_1", 2), ("Plant_1_Big", 1)], 46, 0.8f, 1.25f,
            (x, z) => !Wet(x, z) && MathF.Min(Bed(x, z), PoolDistance(x, z)) < HalfWidth + 3f, sink: 0.05f);

        // No band tree beside the water or between it and the game camera, which looks north-east.
        bool Water(float x, float z) => MathF.Min(Bed(x, z), PoolDistance(x, z)) < HalfWidth + 2.5f;
        d.BarrierBand(mix, s_sides, (x, z) => Wet(x, z) || MathF.Min(Bed(x, z), PoolDistance(x, z)) < HalfWidth + 2f,
            (x, z) => MathF.Min(Bed(x, z), PoolDistance(x, z)) < HalfWidth + 4.5f || InView(x, z, Water));
        Inner(d, mix, saplings: ancient is null ? 2 : 0, understorey: 40, cover: 50, rocks: 3, clear: (x, z) => MathF.Min(Bed(x, z), PoolDistance(x, z)) < HalfWidth + 1.4f,
            treesClear: (x, z) => InView(x, z, Water));
        return Border(name, "stream", 1f, terrain, d);
    }

    // ---- shared dressing ----

    /// <summary>
    /// The floor inside the band: a few kit saplings or snags of the mix (kept under any ancient crown), the darker
    /// understorey, mossy stones and ground cover, none where <paramref name="clear" /> holds, and no sapling where
    /// <paramref name="treesClear" /> does (a feature's line to the camera). Measured from the dresser's origin, so a
    /// cell of a world scene is dressed alike.
    /// </summary>
    private static void Inner(
        SceneryDresser d, TreeMix mix, int saplings, int understorey, int cover, int rocks, Func<float, float, bool>? clear = null,
        Func<float, float, bool>? treesClear = null)
    {
        clear ??= static (_, _) => false;
        treesClear ??= static (_, _) => false;
        for (int planted = 0, attempts = 0; planted < saplings && attempts < 200; attempts++)
        {
            float x = d.Origin.X + d.Uniform(7f, 23f), z = d.Origin.Z + d.Uniform(7f, 23f);
            if (clear(x, z) || treesClear(x, z)) continue;
            TreeSpecies species = ForestDresser.PickSpecies(mix, d.Rng);
            if (species == TreeSpecies.TwistedTree) species = TreeSpecies.DeadTree;
            if (d.TryTree(species, x, z, d.Uniform(0.85f, 1.15f))) planted++;
        }
        d.Understorey(understorey, (x, z) => EdgeDistance(x - d.Origin.X, z - d.Origin.Z) > 3.5f && !clear(x, z));
        d.MossyRocks(rocks, (x, z) => EdgeDistance(x - d.Origin.X, z - d.Origin.Z) > 5f && !clear(x, z));
        d.GroundCover(cover, (x, z) => !clear(x, z));
    }

    /// <summary>
    /// Whether something <paramref name="feature" /> holds lies within 12 m north-east of the point: the point stands
    /// between it and the game camera (pitch 50 degrees, looking north-east at rotation 0), where a tree would hide it.
    /// </summary>
    private static bool InView(float x, float z, Func<float, float, bool> feature)
    {
        const float Step = 1.5f / 1.41421356f;
        for (int k = 0; k <= 8; k++)
        {
            if (feature(x + Step * k, z + Step * k)) return true;
        }
        return false;
    }

    /// <summary>
    /// A fallen log on the ground, sunk a little (it may cross another), its length taken; mushrooms at its side.
    /// </summary>
    private static void Log(SceneryDresser d, string model, float x, float z, int yaw, float scale)
    {
        if (!d.TryAdd(model, x, z, yaw, scale, 0.12f))
            throw new InvalidOperationException($"{d.Name}: log {model} does not fit at ({ForestText.N(x)}, {ForestText.N(z)})");
        (float lx, float lz) = Yaw.Turn(1f, 0f, yaw);
        for (float t = -1.5f; t <= 1.5f; t += 1.5f) d.Take(x + lx * t * scale, z + lz * t * scale, 0.7f * scale);
        (float fx, float fz) = Yaw.Turn(0f, 1f, yaw);
        for (int i = d.Rng.Next(1, 4); i > 0; i--)
        {
            float along = d.Uniform(-1.6f, 1.6f) * scale, side = (d.Rng.Next(2) == 0 ? -1f : 1f) * d.Uniform(0.55f, 0.9f) * scale;
            (float ax, float az) = Yaw.Turn(1f, 0f, yaw);
            d.TryAdd(d.Pick(["Mushroom_Laetiporus", "Mushroom_Common"]), x + ax * along + fx * side, z + az * along + fz * side, d.Rng.Next(360), d.Uniform(0.8f, 1.2f));
        }
    }

    /// <summary>
    /// A mossy boulder outcrop: two boulders shouldering together, mossy stones at their feet, roots gripping over the
    /// larger one.
    /// </summary>
    private static void Outcrop(SceneryDresser d, float x, float z)
    {
        const float Big = 1.05f;
        if (!d.TryPlace("avalon_boulder_4", x, z, 1.9f * Big, Big, 0.2f, yaw: 205))
            throw new InvalidOperationException($"{d.Name}: the outcrop does not fit at ({ForestText.N(x)}, {ForestText.N(z)})");
        d.TryPlace("avalon_boulder_2", x + 2.4f, z - 1.2f, 1.1f, 0.9f, 0.18f, yaw: 140);
        HangOver(d, "avalon_roots_hanging_1", x, z, 1.55f * Big, 200, top: 2.45f * Big - 0.35f);
        d.Cluster(x - 1.5f, z - 2f, ["avalon_rock_mossy_1", "avalon_rock_mossy_3"], 1.2f, 0.6f, 0.95f);
    }

    /// <summary>
    /// Hanging growth (roots or a creeper) over a rock's edge: at <paramref name="reach" /> from the rock's centre
    /// toward the yaw's forward, hanging down from <paramref name="top" /> above the ground there.
    /// </summary>
    private static void HangOver(SceneryDresser d, string model, float x, float z, float reach, int yaw, float top)
    {
        (float fx, float fz) = Yaw.Turn(0f, 1f, yaw);
        float hx = x + fx * (reach - 0.35f), hz = z + fz * (reach - 0.35f);
        d.TryAddAt(model, hx, d.Height(x, z) + top, hz, yaw, 0.9f);
    }

    private static SceneryPiece Border(string name, string family, float weight, SceneryTerrain terrain, SceneryDresser d, bool filler = false) =>
        new(name, terrain, d.Items)
        {
            Kind = SceneryKind.Border,
            Family = family,
            Weight = weight,
            Front = Side.S,
            Filler = filler,
            Kit = DecorJsonWriter.ForestKit,
        };

    // ---- shapes ----

    /// <summary>A ground quad rising more than this across it, in metres, is a rock face's slope: it wears rock.</summary>
    private const float SteepRise = 1.25f;

    /// <summary>
    /// Whether the ground grid's quad holding (x, z) rises more than <see cref="SteepRise" /> across its corners: a face's
    /// slope, whose triangles would otherwise wear the floor at their centroids.
    /// </summary>
    private static bool Steep(Func<float, float, float> height, float x, float z)
    {
        const float S = SceneryTerrain.Step;
        float x0 = MathF.Floor(x / S) * S, z0 = MathF.Floor(z / S) * S;
        float h00 = height(x0, z0), h10 = height(x0 + S, z0), h01 = height(x0, z0 + S), h11 = height(x0 + S, z0 + S);
        return MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11)) - MathF.Min(MathF.Min(h00, h10), MathF.Min(h01, h11)) > SteepRise;
    }

    /// <summary>
    /// The old woods' gentle relief: two octaves of value noise, at most <see cref="ReliefHeight" /> up or down, fading
    /// to exactly 0 at the cell's edge over its first 6 m.
    /// </summary>
    private static float Relief(int seed, float x, float z)
    {
        float noise = (Noise(seed, x, z, 9f) - 0.5f) * 0.7f + (Noise(seed + 1, x, z, 3.5f) - 0.5f) * 0.2f;
        return ReliefHeight / 0.45f * noise * Smooth(0.5f, 6f, EdgeDistance(x, z));
    }
}
