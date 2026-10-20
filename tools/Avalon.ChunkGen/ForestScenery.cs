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
/// Every piece is composed by hand around a few fixed points and dressed from its own random stream (its name's seed),
/// so a rerun writes the same files.
/// </summary>
public static class ForestScenery
{
    public const ushort MapId = ChunkFiles.ForestMapId;

    /// <summary>The ravine's floor, below the forest's mist floor (-10 m).</summary>
    public const float RavineFloor = -15f;

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
    /// The cliff face sections (forest-kit NOTES and its strata script): length and height at scale 1, how far the
    /// middle bulges out, how far the ends turn back, how far it leans back over its height.
    /// </summary>
    private static readonly (string Model, float Length, float Height, float Bulge, float Ends, float Lean)[] s_faces =
    [
        ("avalon_cliff_face_1", 7f, 6.4f, 0.18f, 0.6f, 0.75f),
        ("avalon_cliff_face_2", 6.2f, 7.2f, 0.25f, 0.65f, 0.6f),
        ("avalon_cliff_face_3", 7.8f, 7.8f, 0.15f, 0.55f, 0.9f),
    ];

    /// <summary>The ravine lip sections and their lengths at scale 1.</summary>
    private static readonly (string Model, float Length)[] s_lips = [("avalon_ravine_lip_1", 6f), ("avalon_ravine_lip_2", 5.2f), ("avalon_ravine_lip_3", 6.8f)];

    /// <summary>How far two face or lip sections overlap end to end, in metres.</summary>
    private const float SectionOverlap = 0.9f;

    /// <summary>
    /// The most two face sections overlap, where their least scales make them longer than their side.
    /// </summary>
    private const float MaxOverlap = 3.5f;

    /// <summary>
    /// A face section's foot stands this far out from the plateau's rough face, which stands this far out from the
    /// plateau's top.
    /// </summary>
    private const float FaceToWall = 2.2f, WallToTop = 0.4f;

    /// <summary>The scale range a cliff face section is drawn at.</summary>
    private const float FaceMinScale = 0.85f, FaceMaxScale = 1.35f;

    private static readonly string[] s_scree = ["Pebble_Round_1", "Pebble_Round_3", "Pebble_Round_5", "Pebble_Round_2"];

    private static readonly string[] s_mossyRocks = ["avalon_rock_mossy_1", "avalon_rock_mossy_2", "avalon_rock_mossy_3"];

    /// <summary>Every forest scenery piece: the eleven borders and the eight exit caps.</summary>
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
        Cluster(d, 21.5f, 20.5f, ["avalon_rock_mossy_1", "avalon_rock_mossy_2", "avalon_rock_mossy_3", "avalon_rock_mossy_2"], 2.6f, 0.8f, 1.25f);
        Cluster(d, 8f, 17f, ["avalon_bramble_2", "avalon_bramble_1", "avalon_bramble_1"], 2.2f, 0.9f, 1.15f);
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
        Cluster(d, 15f, 15f, ["avalon_rock_mossy_3", "avalon_rock_mossy_1"], 1.6f, 0.8f, 1.1f);
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
        Cluster(d, 10.2f, 14.6f, ["avalon_rock_mossy_2", "avalon_rock_mossy_1", "avalon_rock_mossy_3"], 1.3f, 0.7f, 1.05f);
        float Rim(float x, float z) => SceneryDresser.Distance(x, z, GX, GZ) - Radius;
        d.Scatter([("Fern_1", 6), ("Plant_1_Big", 1), ("Grass_Wispy_Tall", 2)], 22, 0.85f, 1.3f, (x, z) => Rim(x, z) is > -1.4f and < 0.6f);
        d.Scatter([("Grass_Common_Short", 6), ("Grass_Wispy_Short", 5), ("Clover_1", 3), ("Clover_2", 3), ("Flower_3_Group", 1), ("Flower_4_Single", 1),
            ("Plant_7", 2)], 34, 0.7f, 1.05f, (x, z) => Rim(x, z) < -1.2f, spread: 0.35f);
        if (!d.TryPlace("avalon_boulder_3", 21f, 9.5f, 2f, 1f, 0.2f, yaw: 205))
            throw new InvalidOperationException($"{d.Name}: the boulder does not fit at (21, 9.5)");
        HangOver(d, "avalon_creeper_1", 21f, 9.5f, 1.46f, 205, top: 2.41f - 0.3f);
        Cluster(d, 9.5f, 21.5f, ["avalon_bramble_2", "avalon_hedge_dark", "avalon_bramble_1", "avalon_hedge_dark", "avalon_bramble_2"], 2.8f, 0.9f, 1.25f);
    }

    // ---- mossy cliffs ----

    /// <summary>
    /// One tier of a cliff plateau: its foot (convex, counter-clockwise: where its face sections stand), its top's
    /// height, and the side (foot[Ramp] to the next corner) that falls back to the ground as an earth bank instead of a
    /// face, if any. Tiers overlap; the ground is the highest of them.
    /// </summary>
    private sealed record CliffTier(Outline Foot, float Top, int? Ramp = null)
    {
        /// <summary>The top's edge: the foot moved in to where the face sections' turf caps end.</summary>
        public Outline Edge { get; } = Inset(Foot, FaceToWall + WallToTop);

        /// <summary>
        /// The tier's ground at a point (0 off it) and whether that is its rock band: the top, a 1 m band falling from
        /// it under the faces, and the earth bank behind its ramp side, falling over <see cref="RampLength" /> and
        /// fading off over <see cref="RampFade" /> past the side's ends, where its steep flanks are rock.
        /// </summary>
        public (float Height, bool Rock) Sample(float x, float z)
        {
            if (Inside(Edge, x, z)) return (Top, false);
            float beyond = OutlineDistance(Edge, x, z);
            float band = beyond < 1f ? Top * (1f - beyond) : 0f;
            float bank = 0f;
            bool flank = false;
            if (Ramp is int k)
            {
                (float X, float Z) a = Edge[k], b = Edge[(k + 1) % Edge.Count];
                float length = SceneryDresser.Distance(a.X, a.Z, b.X, b.Z);
                float tx = (b.X - a.X) / length, tz = (b.Z - a.Z) / length;
                float outward = (x - a.X) * tz - (z - a.Z) * tx;
                float along = (x - a.X) * tx + (z - a.Z) * tz;
                if (outward > 0f)
                {
                    float past = MathF.Max(MathF.Max(-along, along - length), 0f);
                    bank = Top * (1f - Smooth(0f, RampLength, outward)) * (1f - Smooth(0f, RampFade, past));
                    flank = past > 0f;
                }
            }
            // The bank's flanks past its side's ends fall steeply beside the faces: rock, as the faces are.
            return bank >= band ? (bank, flank && bank > 0.3f) : (band, band > 0.05f);
        }
    }

    /// <summary>
    /// How far an earth bank behind a cliff's top runs out to the ground, and how far it fades off past its side's
    /// ends.
    /// </summary>
    private const float RampLength = 9f, RampFade = 4.5f;

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
    /// How much longer than its side (and its corners' reach) a single face section may be, in metres.
    /// </summary>
    private const float MaxOvershoot = 2f;

    /// <summary>The most a face section's top stands above its tier's rim, in metres: a crag.</summary>
    private const float MaxCrag = 0.6f;

    private static SceneryPiece Cliffs(int n, IReadOnlyDictionary<string, ModelBounds> kit, TreeMix mix, CliffTier[] tiers, bool ancientOnTop)
    {
        string name = $"forest_ring_cliffs_{n:00}";
        foreach (CliffTier tier in tiers) Convex(name, tier.Foot);
        int seed = ForestDresser.StableSeed(name);

        float FootDistance(float x, float z) => tiers.Min(t => Inside(t.Foot, x, z) ? 0f : OutlineDistance(t.Foot, x, z));
        (float Height, bool Rock) Sample(float x, float z)
        {
            (float Height, bool Rock) best = (0f, false);
            foreach (CliffTier tier in tiers)
            {
                (float h, bool rock) = tier.Sample(x, z);
                if (h > best.Height) best = (h, rock);
            }
            // The woods' relief off the cliff, faded out toward its foot and under any bank.
            float relief = Relief(seed, x, z) * Smooth(2f, 5f, FootDistance(x, z)) * (1f - Smooth(0f, 0.5f, best.Height));
            return (best.Height + relief, best.Rock);
        }
        float Height(float x, float z) => Sample(x, z).Height;

        List<SceneryWall> walls = [];
        foreach (CliffTier tier in tiers)
        {
            Outline wall = Inset(tier.Foot, FaceToWall);
            // A ramp side has no rough face: the wall runs open round the others.
            walls.Add(tier.Ramp is int k
                ? new SceneryWall([.. Enumerable.Range(1, wall.Count).Select(i => wall[(k + i) % wall.Count])], Closed: false, Bottom: -0.6f, Top: tier.Top, Rock)
                : new SceneryWall(wall, Closed: true, Bottom: -0.6f, Top: tier.Top, Rock));
        }
        // A steep quad round a face's foot is rock whatever the tiers say, so no steep floor shows between face sections.
        var terrain = new SceneryTerrain(Height, (x, z) => Sample(x, z).Rock || Steep(Height, x, z) ? Rock : Floor) { Walls = walls };
        var d = new SceneryDresser(name, terrain, kit);

        // The faces: layered sections end to end along each side of each tier but a ramp, with ledges, roots and
        // creepers; a section stands from the ground in front of it, and one a higher tier buries is left out.
        foreach (CliffTier tier in tiers)
        {
            for (int i = 0; i < tier.Foot.Count; i++)
            {
                if (i == tier.Ramp) continue;
                FaceSections(d, tier.Foot[i], tier.Foot[(i + 1) % tier.Foot.Count], tier.Top,
                    (x, z) => tiers.Any(other => other.Top > tier.Top && Inside(other.Edge, x, z)));
            }
        }

        // A bank's steep rock flanks: a ledge out of each and a mossy boulder at its foot.
        foreach (CliffTier tier in tiers)
        {
            if (tier.Ramp is int ramp) Flanks(d, tier, ramp, Height);
        }

        // On top: an ancient tree on the highest tier, or a stand of kit trees, undergrowth to the lip; ferns on a bank.
        bool Flat(float x, float z, float least)
        {
            float h = Height(x, z);
            return h >= least && MathF.Abs(Height(x + 0.8f, z) - h) < 0.05f && MathF.Abs(Height(x - 0.8f, z) - h) < 0.05f
                && MathF.Abs(Height(x, z + 0.8f) - h) < 0.05f && MathF.Abs(Height(x, z - 0.8f) - h) < 0.05f;
        }
        bool OnTop(float x, float z, float margin) =>
            Flat(x, z, 1f) && tiers.Any(t => Inside(t.Edge, x, z) && OutlineDistance(t.Edge, x, z) >= margin);
        CliffTier highest = tiers.MaxBy(t => t.Top)!;
        (float cx, float cz) = Centroid(highest.Edge);
        if (ancientOnTop && !d.TryAncient(1 + d.Rng.Next(4), cx, cz, d.Rng.Next(360)))
            throw new InvalidOperationException($"{name}: no ancient tree fits on top at ({ForestText.N(cx)}, {ForestText.N(cz)})");
        for (int planted = 0, attempts = 0; planted < (ancientOnTop ? 3 : 6) && attempts < 400; attempts++)
        {
            float x = d.Uniform(0f, Cell), z = d.Uniform(0f, Cell);
            if (!OnTop(x, z, 1.4f)) continue;
            TreeSpecies species = ForestDresser.PickSpecies(mix, d.Rng);
            if (species == TreeSpecies.TwistedTree) species = TreeSpecies.Pine;
            if (d.TryTree(species, x, z, d.Uniform(0.85f, 1.25f))) planted++;
        }
        d.Understorey(ancientOnTop ? 30 : 34, (x, z) => OnTop(x, z, 0.9f), 0.7f, 1.2f);
        d.GroundCover(24, (x, z) => OnTop(x, z, 0.5f));
        d.Scatter([("Fern_1", 5), ("Grass_Wispy_Tall", 3), ("Plant_1", 2), ("avalon_hedge_dark", 1)], 26, 0.8f, 1.2f,
            (x, z) => Sample(x, z) is { Rock: false, Height: > 0.4f } && !Flat(x, z, 0f), sink: 0.15f);

        // At the foot of every face: scree of pebbles and small rocks, ferns among it, on whatever level ground is there.
        foreach (CliffTier tier in tiers)
        {
            for (int i = 0; i < tier.Foot.Count; i++)
            {
                if (i != tier.Ramp) Scree(d, tier.Foot[i], tier.Foot[(i + 1) % tier.Foot.Count], (x, z) => Flat(x, z, -1f));
            }
        }

        bool Off(float x, float z, float keep) => FootDistance(x, z) >= keep;
        d.BarrierBand(mix, s_sides, (x, z) => !Off(x, z, 2.8f));
        d.Understorey(26, (x, z) => Off(x, z, 0.6f) && EdgeDistance(x, z) > 3f && Flat(x, z, -1f));
        d.GroundCover(30, (x, z) => Off(x, z, 0.3f));
        return Border(name, "cliffs", 1f, terrain, d);
    }

    /// <summary>
    /// Face sections along a side of a tier's foot (from <paramref name="a" /> to <paramref name="b" />, the outline
    /// running counter-clockwise so the face looks out), end to end with at least <see cref="SectionOverlap" />,
    /// reaching a little past each corner. Each rises from the ground in front of it to the tier's top, its own top up
    /// to <see cref="MaxCrag" /> above or a little below the rim so neighbouring sections' strata never run round as
    /// one band, and is sunk so its top stands there. The chain of sections is the one whose scales come nearest 1; a
    /// section whose foot <paramref name="buried" /> holds, or that would rise less than a metre, is left out.
    /// </summary>
    private static void FaceSections(SceneryDresser d, (float X, float Z) a, (float X, float Z) b, float top, Func<float, float, bool> buried)
    {
        float length = SceneryDresser.Distance(a.X, a.Z, b.X, b.Z);
        (float tx, float tz) = ((b.X - a.X) / length, (b.Z - a.Z) / length);
        (float nx, float nz) = (tz, -tx);                       // outward
        int yaw = SceneryDresser.YawToward(nx, nz);
        (float ax, float az) = Yaw.Turn(1f, 0f, yaw);            // the section's own +X
        float target = length + 0.3f;
        float Base(float along) => d.Height(a.X + tx * along + nx * 1.2f, a.Z + tz * along + nz * 1.2f);
        // The most the side's sections must rise, with room for the tallest crag above the rim.
        float rise = Enumerable.Range(0, 9).Max(i => top - Base(length * i / 8f)) + MaxCrag / 2f;

        List<int> best = [];
        float bestScore = float.MaxValue, bestFactor = 1f, bestOverlap = SectionOverlap;
        for (int k = 1; k <= 3; k++)
        {
            foreach (int[] chain in Chains(k))
            {
                // At their least scales the sections may be longer than the side: they overlap more, up to MaxOverlap.
                float least = chain.Sum(c => s_faces[c].Length * MinFaceScale(c, rise));
                float overlap = k == 1 ? 0f : MathF.Max(SectionOverlap, (least - target) / (k - 1));
                if (overlap > MaxOverlap) continue;
                float factor = (target + overlap * (k - 1)) / least;
                // A single section a little longer than its side reaches further past the corners.
                if (k == 1 && factor < 1f && least - target <= MaxOvershoot) factor = 1f;
                if (factor < 1f - 0.001f || chain.Any(c => MinFaceScale(c, rise) * factor > FaceMaxScale)) continue;
                float score = chain.Sum(c => MathF.Abs(MinFaceScale(c, rise) * factor - 1.05f)) / k + 0.08f * k
                    + 0.1f * (overlap - SectionOverlap) + 0.15f * Repeats(chain) + d.Uniform(0f, 0.12f);
                if (score < bestScore) (best, bestScore, bestFactor, bestOverlap) = ([.. chain], score, factor, overlap);
            }
        }
        if (best.Count == 0)
            throw new InvalidOperationException($"{d.Name}: no chain of face sections spans the {ForestText.N(length)} m side from ({ForestText.N(a.X)}, {ForestText.N(a.Z)})");

        float[] scales = [.. best.Select(c => SceneryDresser.Round(MinFaceScale(c, rise) * bestFactor))];
        float total = best.Select((c, i) => s_faces[c].Length * scales[i]).Sum() - bestOverlap * (best.Count - 1);
        float cursor = (length - total) / 2f;
        for (int i = 0; i < best.Count; i++)
        {
            (string model, float sectionLength, float rock, float bulge, float ends, float lean) = s_faces[best[i]];
            float scale = scales[i];
            float along = cursor + sectionLength * scale / 2f;
            cursor += sectionLength * scale - bestOverlap;
            float x = a.X + tx * along, z = a.Z + tz * along, ground = Base(along);
            if (buried(x, z) || top - ground < 1f) continue;
            // Its top a little above or below the rim, never so high that its foot would show above the ground.
            float crag = MathF.Min(d.Uniform(-0.25f, MaxCrag), rock * scale - (top - ground) - 0.1f);
            float rim = top + crag, y = rim - rock * scale;
            if (!d.TryAddAt(model, x, y, z, yaw, scale))
                throw new InvalidOperationException($"{d.Name}: face section {model} at ({ForestText.N(x)}, {ForestText.N(z)}) crosses the cell's edge");

            // A point on the face at its own (u across -1..1, h up in its metres): its plan bulges, its ends turn back, it leans back.
            (float X, float Z) OnFace(float u, float h, float back)
            {
                float plan = bulge * (1f - u * u) - ends * TurnBack(Smooth(0.62f, 1f, MathF.Abs(u))) - lean * h / rock - back;
                float lx = u * sectionLength / 2f;
                return (x + (ax * lx + nx * plan) * scale, z + (az * lx + nz * plan) * scale);
            }

            // Ledges a third to two thirds down what shows of it, one or two, apart.
            float shows = (rim - ground) / scale;
            int ledges = shows < 2.5f ? 0 : d.Rng.NextDouble() < 0.8 ? 1 + (d.Rng.NextDouble() < 0.45 ? 1 : 0) : 0;
            float lastU = 9f;
            for (int l = 0; l < ledges; l++)
            {
                float u = d.Uniform(-0.45f, 0.45f);
                if (MathF.Abs(u - lastU) < 0.45f) u = -u;
                lastU = u;
                float h = rock - shows * d.Uniform(0.32f, 0.7f);
                (float lx, float lz) = OnFace(u, h, 0f);
                d.TryAddAt(d.Pick(["avalon_cliff_ledge_1", "avalon_cliff_ledge_2"]), lx, y + h * scale, lz, yaw + d.Rng.Next(-6, 7), d.Uniform(0.85f, 1.1f));
            }

            // Roots gripping over the lip and hanging down the face; a creeper beside them.
            if (d.Rng.NextDouble() < 0.75)
            {
                float u = d.Uniform(-0.5f, 0.5f);
                (float rx, float rz) = OnFace(u, rock, 0.05f);
                d.TryAddAt("avalon_roots_hanging_1", rx, rim, rz, yaw + d.Rng.Next(-10, 11), d.Uniform(0.9f, 1.15f));
                float v = u > 0f ? u - d.Uniform(0.35f, 0.6f) : u + d.Uniform(0.35f, 0.6f);
                if (d.Rng.NextDouble() < 0.55)
                {
                    (float cxx, float czz) = OnFace(v, rock, 0.05f);
                    d.TryAddAt("avalon_creeper_1", cxx, rim, czz, yaw + d.Rng.Next(-8, 9), d.Uniform(0.9f, 1.15f));
                }
            }
        }
    }

    /// <summary>
    /// The rock flanks beside a bank (past each end of the tier's ramp side, where the bank falls steeply beside the
    /// faces): on each, two ledges standing out of the slope a third and two thirds up, facing down it, and a mossy
    /// boulder sunk into its foot, so the heightfield's rock there is not one smooth plane. The spots are the steepest flank points
    /// nearest those heights on a fixed grid; a flank too small to hold them is left as it is.
    /// </summary>
    private static void Flanks(SceneryDresser d, CliffTier tier, int ramp, Func<float, float, float> height)
    {
        (float X, float Z) a = tier.Edge[ramp], b = tier.Edge[(ramp + 1) % tier.Edge.Count];
        float length = SceneryDresser.Distance(a.X, a.Z, b.X, b.Z);
        (float tx, float tz) = ((b.X - a.X) / length, (b.Z - a.Z) / length);
        (float nx, float nz) = (tz, -tx);                       // outward, the bank's way
        (float X, float Z) Downhill(float x, float z) => (height(x - 0.5f, z) - height(x + 0.5f, z), height(x, z - 0.5f) - height(x, z + 0.5f));
        foreach (((float X, float Z) end, float away) in new[] { (a, -1f), (b, 1f) })
        {
            List<(float X, float Z, float H, float Slope)> flank = [];
            for (float past = 0.5f; past <= 4.5f; past += 0.5f)
            {
                for (float outward = 0.5f; outward <= 8f; outward += 0.5f)
                {
                    float x = end.X + tx * away * past + nx * outward, z = end.Z + tz * away * past + nz * outward;
                    if (!tier.Sample(x, z).Rock) continue;
                    (float gx, float gz) = Downhill(x, z);
                    flank.Add((x, z, height(x, z), MathF.Sqrt(gx * gx + gz * gz)));
                }
            }
            List<(float X, float Z)> used = [];
            (float X, float Z, float H, float Slope)? Spot(float target, float least)
            {
                (float X, float Z, float H, float Slope)? best = null;
                foreach ((float X, float Z, float H, float Slope) p in flank)
                {
                    if (p.Slope < least || used.Any(u => SceneryDresser.Distance(p.X, p.Z, u.X, u.Z) < 2.2f)) continue;
                    if (best is not { } b || MathF.Abs(p.H - target) - 0.1f * p.Slope < MathF.Abs(b.H - target) - 0.1f * b.Slope)
                        best = p;
                }
                if (best is { } spot) used.Add((spot.X, spot.Z));
                return best;
            }

            // Two ledges, a third and two thirds up, one of each model.
            int first = d.Rng.Next(2);
            for (int l = 0; l < 2; l++)
            {
                if (Spot(tier.Top * (l == 0 ? 0.4f : 0.72f), 1f) is not { } ledge) continue;
                (float gx, float gz) = Downhill(ledge.X, ledge.Z);
                d.TryAddAt((l + first) % 2 == 0 ? "avalon_cliff_ledge_1" : "avalon_cliff_ledge_2", ledge.X, ledge.H + 0.05f, ledge.Z,
                    SceneryDresser.YawToward(gx, gz) + d.Rng.Next(-8, 9), d.Uniform(0.9f, 1.1f));
            }
            if (Spot(1.1f, 0.6f) is { } foot)
            {
                (float gx, float gz) = Downhill(foot.X, foot.Z);
                float low = MathF.Min(MathF.Min(height(foot.X - 1f, foot.Z), height(foot.X + 1f, foot.Z)), MathF.Min(height(foot.X, foot.Z - 1f), height(foot.X, foot.Z + 1f)));
                if (d.TryAddAt("avalon_boulder_3", foot.X, low - 0.2f, foot.Z, SceneryDresser.YawToward(gx, gz) + d.Rng.Next(-20, 21), d.Uniform(0.95f, 1.15f)))
                    d.Take(foot.X, foot.Z, 2f);
            }
        }
    }

    /// <summary>
    /// How far a face section's ends have turned back at a share of their reach: the strata script's t^1.4, drawn as
    /// t^1.5 (a square root, which every machine rounds alike, where a power does not).
    /// </summary>
    private static float TurnBack(float t) => t * MathF.Sqrt(t);

    /// <summary>
    /// The least scale a face section is drawn at to rise <paramref name="rise" /> metres: its rock spans it.
    /// </summary>
    private static float MinFaceScale(int face, float rise) => MathF.Max(FaceMinScale, rise / s_faces[face].Height);

    /// <summary>Every sequence of <paramref name="k" /> face sections.</summary>
    private static IEnumerable<int[]> Chains(int k) =>
        k == 1 ? Enumerable.Range(0, s_faces.Length).Select(i => new[] { i }) : Chains(k - 1).SelectMany(shorter => Enumerable.Range(0, s_faces.Length).Select(i => (int[])[.. shorter, i]));

    /// <summary>How many times a section stands beside the same section in the chain.</summary>
    private static int Repeats(int[] chain) => Enumerable.Range(1, chain.Length - 1).Count(i => chain[i] == chain[i - 1]);

    /// <summary>
    /// Scree along a side of the foot: pebbles mostly, small rocks and mossy stones, thinning out from the face; ferns
    /// and grass among it.
    /// </summary>
    private static void Scree(SceneryDresser d, (float X, float Z) a, (float X, float Z) b, Func<float, float, bool> level)
    {
        float length = SceneryDresser.Distance(a.X, a.Z, b.X, b.Z);
        (float tx, float tz) = ((b.X - a.X) / length, (b.Z - a.Z) / length);
        (float nx, float nz) = (tz, -tx);
        for (float along = 0.2f; along < length; along += d.Uniform(0.35f, 0.8f))
        {
            float r = d.Uniform(0f, 1f);
            float beyond = 0.2f + 2.6f * r * r;
            float x = a.X + tx * along + nx * beyond, z = a.Z + tz * along + nz * beyond;
            double roll = d.Rng.NextDouble();
            if (!level(x, z)) continue;
            if (roll < 0.5)
            {
                float scale = d.Uniform(1.2f, 2.4f) * (1.2f - r * 0.5f);
                d.TryPlace(d.Pick(s_scree), x, z, 0.22f * scale, scale, 0.02f);
            }
            else if (roll < 0.66)
            {
                float scale = d.Uniform(0.18f, 0.36f);
                d.TryPlace(d.Pick(["Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3"]), x, z, 1.4f * scale, scale, 0.08f);
            }
            else if (roll < 0.86)
            {
                float scale = d.Uniform(0.35f, 0.8f) * (1.15f - r * 0.4f);
                d.TryPlace(d.Pick(s_mossyRocks), x, z, 0.9f * scale, scale, 0.05f);
            }
            else
            {
                string model = d.Pick(["Fern_1", "Grass_Wispy_Tall", "Fern_1", "Plant_1"]);
                d.TryPlace(model, x + nx * 0.4f, z + nz * 0.4f, d.Spacing(model, 0.9f), d.Uniform(0.75f, 1.1f));
            }
        }
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
        // Along the brink: ferns, grass and the odd bush, as the spike's rim had; then the floor between it and the band.
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
        // Its +X, root end to crown end, runs west, 10 degrees off the x axis: across the game camera's view (which looks
        // north-east), as the spike laid its giants along a cell edge, so the whole trunk and the plate's face show.
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
            float scale = i < 6 ? d.Uniform(1.4f, 2.6f) : model == "Rock_Medium_2" ? d.Uniform(0.22f, 0.35f) : d.Uniform(0.45f, 0.65f);
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
        Cluster(d, sx, sz, ["avalon_rock_mossy_2", "avalon_rock_mossy_1", "avalon_rock_mossy_3", "avalon_rock_mossy_1"], 1.9f, 0.6f, 1f, keep: 0.7f);

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
            Cluster(d, ex, ez, ["avalon_rock_mossy_2", "avalon_rock_mossy_3", "Rock_Medium_1", "avalon_rock_mossy_1"], 2f, 0.55f, 0.95f, keep: 0.6f);
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

    // ---- exit caps ----

    /// <summary>What ends an open exit's path at a capped edge.</summary>
    private enum Obstacle
    {
        Rockfall,
        Trunk,
        Thicket,
        Brink,
    }

    /// <summary>
    /// A capped edge and what ends its path there; for a brink, its ravine's brink (clockwise) and the dead trees on its
    /// floor, in the edge's own frame (<see cref="CapEdge" />).
    /// </summary>
    private sealed record CapSide(Side Side, Obstacle Obstacle, Outline? Ravine = null, (string Model, float U, float V, float Scale)[]? Dead = null)
    {
        public CapEdge Edge => new(Side);
    }

    /// <summary>
    /// A capped edge's frame: u along the edge and v in from it, as the S edge's x and z, turned onto its side as the ring
    /// turns a piece about the cell's centre (a quarter turn takes S to W, ChunkRotation's), so every obstacle is
    /// composed once, against the S edge.
    /// </summary>
    private readonly record struct CapEdge(Side Side)
    {
        private int Turns => Side switch { Side.S => 0, Side.W => 1, Side.N => 2, _ => 3 };

        /// <summary>The piece-local point at (u, v) of this edge's frame.</summary>
        public (float X, float Z) At(float u, float v)
        {
            float x = u - Half, z = v - Half;
            for (int i = 0; i < Turns; i++) (x, z) = (z, -x);
            return (x + Half, z + Half);
        }

        /// <summary>The (u, v) of a piece-local point in this edge's frame.</summary>
        public (float U, float V) Of(float x, float z)
        {
            float u = x - Half, v = z - Half;
            for (int i = 0; i < Turns; i++) (u, v) = (-v, u);
            return (u + Half, v + Half);
        }

        /// <summary>A yaw in this edge's frame, turned onto the piece.</summary>
        public int Turn(int yaw) => Yaw.Normal(yaw + 90 * Turns);
    }

    private const float Half = ChunkPiece.CellSize / 2f;

    /// <summary>No kit or ancient tree stands nearer a capped edge's midpoint than this (the rules' 10 m, and a margin).</summary>
    private const float CapClear = SceneryRules.CapClearing + 0.4f;

    /// <summary>
    /// How many path segments carry the exit's path in from a capped edge (2 m each, every one
    /// <see cref="ForestPaths.Advance" /> on from the last, as a chunk lays them), and how far in it shows before the
    /// obstacle covers it. A brink's path breaks off at the lip, 2 m in.
    /// </summary>
    private static (int Segments, float Shows) Stub(Obstacle obstacle) => obstacle switch
    {
        Obstacle.Brink => (1, 2f),
        Obstacle.Trunk => (3, 3.4f),
        Obstacle.Thicket => (3, 4.2f),
        _ => (3, 3f),
    };

    /// <summary>The giant across a trunk cap's path: its middle, in its edge's frame, and its yaw (+X root end to crown end).</summary>
    private const float TrunkU = 14.2f, TrunkV = 4.9f;

    private const int TrunkYaw = 165;

    /// <summary>
    /// A trunk cap's giant, in its edge's frame: its middle, its root and crown ends, its root plate (behind its root end,
    /// 6 m or more in from every edge) and the torn earth beyond the plate.
    /// </summary>
    private static TrunkLayout TrunkPlan()
    {
        const float HalfLength = 9.1f, PlateBack = 0.25f;
        (float du, float dv) = Yaw.Turn(1f, 0f, TrunkYaw);
        (float U, float V) plate = (TrunkU - du * (HalfLength + PlateBack), TrunkV - dv * (HalfLength + PlateBack));
        return new((TrunkU, TrunkV), (TrunkU - du * HalfLength, TrunkV - dv * HalfLength), (TrunkU + du * HalfLength, TrunkV + dv * HalfLength),
            plate, (plate.U - du * 1.8f, plate.V - dv * 1.8f));
    }

    private readonly record struct TrunkLayout((float U, float V) Middle, (float U, float V) Root, (float U, float V) Crown, (float U, float V) Plate, (float U, float V) Hollow);

    /// <summary>
    /// The rockfall heap, in its edge's frame: a back row of big boulders 6.5 m and more in (scale 1.3-1.45, outside the
    /// band), one heaved up onto them (its top near 4.5 m), and a front row in the band, its tops under 4 m, whose middle
    /// stone lies over the path's end; a boulder fallen at the path's side. A lift above 0 sets a stone that far above the
    /// ground instead of sinking it.
    /// </summary>
    private static readonly (string Model, float U, float V, int Yaw, float Scale, float Lift)[] s_rockfall =
    [
        ("avalon_boulder_4", 13.2f, 7.9f, 160, 1.42f, 0f),
        ("avalon_boulder_3", 17.5f, 8.2f, 35, 1.38f, 0f),
        ("avalon_boulder_4", 15.5f, 10.5f, 250, 1.45f, 0f),
        ("avalon_boulder_2", 10.3f, 7.2f, 110, 1.3f, 0f),
        ("avalon_boulder_2", 20.3f, 7f, 300, 1.32f, 0f),
        ("avalon_boulder_3", 15.3f, 8.3f, 200, 1.1f, 1.75f),
        ("avalon_boulder_3", 8.4f, 9.6f, 80, 1.3f, 0f),
        ("avalon_boulder_4", 22.3f, 9.4f, 330, 1.32f, 0f),
        ("avalon_boulder_2", 19.4f, 9.6f, 140, 1.05f, 1.2f),
        ("avalon_boulder_4", 15.3f, 4.9f, 20, 1.15f, 0f),
        ("avalon_boulder_3", 12f, 4.4f, 290, 1.05f, 0f),
        ("avalon_boulder_2", 18.5f, 4.3f, 75, 1.1f, 0f),
        ("avalon_boulder_1", 13.3f, 2.6f, 140, 0.85f, 0f),
        ("avalon_boulder_1", 20.5f, 2.9f, 40, 0.95f, 0f),
    ];

    /// <summary>The brink cap's ravine, in its edge's frame: a broad pit whose south side runs 2 m from the edge's middle.</summary>
    private static readonly (float X, float Z)[] s_brinkRavine =
    [
        (7.2f, 15.8f), (10.5f, 19.6f), (14.6f, 18.4f), (18.2f, 21.2f), (22.4f, 18.6f), (24.6f, 14.2f), (23.2f, 10.4f), (24.4f, 7f),
        (21.6f, 4.6f), (17.6f, 2f), (12.4f, 2f), (9f, 3.6f), (6.4f, 6.8f), (7.6f, 10.2f), (5.8f, 12.8f),
    ];

    /// <summary>The four-sided cap's ravine, in its edge's frame: a ragged gorge along the edge, 4-7 m wide, 2 m from its middle.</summary>
    private static readonly (float X, float Z)[] s_capGorge =
    [
        (6.8f, 6.6f), (9.6f, 8.8f), (12.8f, 7.8f), (16.4f, 9.4f), (20.2f, 8.4f), (23.4f, 6.2f), (22f, 3.6f), (18.2f, 2.6f), (16.2f, 2f),
        (13.8f, 2f), (10.8f, 2.6f), (8f, 3.2f), (6.4f, 4.6f),
    ];

    /// <summary>
    /// An exit cap: on every capped side the exit's path carries on from the edge's midpoint as a dirt stub and ends
    /// under the side's obstacle, which covers the whole edge band; no tree within <see cref="CapClear" /> of a capped
    /// midpoint; then the given ancient trees and young trees beyond the clearings, the band on all four sides, the floor.
    /// The sides are composed in the order given (a later obstacle fills round an earlier one).
    /// </summary>
    private static SceneryPiece Cap(
        string name, string family, IReadOnlyDictionary<string, ModelBounds> kit, TreeMix mix, CapSide[] sides, (int Variant, float X, float Z)[] ancients,
        int youngTrees)
    {
        int seed = ForestDresser.StableSeed(name);
        CapSide? brinkSide = sides.FirstOrDefault(s => s.Obstacle == Obstacle.Brink);
        Outline? ravine = brinkSide is { Ravine: { } r } ? [.. r.Select(p => brinkSide.Edge.At(p.X, p.Z))] : null;
        if (ravine is not null && SignedArea(ravine) >= 0f)
            throw new InvalidOperationException($"{name}: the brink must run clockwise, so its wall looks into the ravine");
        TrunkLayout plan = TrunkPlan();
        List<(float X, float Z)> hollows = [.. sides.Where(s => s.Obstacle == Obstacle.Trunk).Select(s => s.Edge.At(plan.Hollow.U, plan.Hollow.V))];
        List<((float X, float Z) From, (float X, float Z) To)> stubs = [];
        foreach (CapSide side in sides)
        {
            (int segments, _) = Stub(side.Obstacle);
            stubs.Add((side.Edge.At(Half, 0f), side.Edge.At(Half, 2f + ForestPaths.Advance * (segments - 1))));
        }

        float RimDistance(float x, float z) => ravine is null ? float.MaxValue : OutlineDistance(ravine, x, z);
        bool InRavine(float x, float z) => ravine is not null && Inside(ravine, x, z);
        float PathDistance(float x, float z) => stubs.Min(s => SegmentDistance(x, z, s.From, s.To));
        float Hole(float x, float z) =>
            hollows.Sum(h => -0.6f * (1f - Smooth(1f, 2.6f, SceneryDresser.Distance(x, z, h.X, h.Z))));
        float HoleDistance(float x, float z) => hollows.Count == 0 ? float.MaxValue : hollows.Min(h => SceneryDresser.Distance(x, z, h.X, h.Z));
        float Height(float x, float z)
        {
            if (InRavine(x, z)) return RavineFloor * MathF.Min(1f, RimDistance(x, z));
            // The woods' relief, flat under the path (its segments lie on y 0, as a chunk's do) and fading off the
            // ravine's rim and the torn earth.
            return Relief(seed, x, z) * Smooth(2f, 5f, RimDistance(x, z)) * Smooth(1.6f, 3.6f, PathDistance(x, z)) * Smooth(2.6f, 4.6f, HoleDistance(x, z))
                + Hole(x, z);
        }
        string Material(float x, float z) =>
            (Height(x, z) < -0.3f && InRavine(x, z)) || Steep(Height, x, z) ? Rock : Hole(x, z) < -0.12f ? "dirt_road" : Floor;
        var terrain = new SceneryTerrain(Height, Material)
        {
            Walls = ravine is null ? [] : [RavineWall(ravine)],
            Surfaces = ravine is null ? [] : MistLayers(ravine),
        };
        var d = new SceneryDresser(name, terrain, kit);

        // The path where it still shows, and the ravine with its rim: nothing is dressed there.
        bool OnPath(float x, float z, float margin) => sides.Any(s =>
        {
            (float u, float v) = s.Edge.Of(x, z);
            return v > -margin && v < Stub(s.Obstacle).Shows + margin && MathF.Abs(u - Half) < ForestPaths.Width / 2f + margin;
        });
        bool Free(float x, float z) => !OnPath(x, z, 0.3f) && !InRavine(x, z) && RimDistance(x, z) >= 1.5f;
        bool Clearing(float x, float z) => sides.Any(s =>
        {
            (float mx, float mz) = s.Edge.At(Half, 0f);
            return SceneryDresser.Distance(x, z, mx, mz) < CapClear;
        });

        foreach (CapSide side in sides) Path(d, side.Edge, Stub(side.Obstacle).Segments);
        foreach ((int variant, float x, float z) in ancients)
        {
            if (!d.TryAncient(variant, x, z, d.Rng.Next(360)))
                throw new InvalidOperationException($"{name}: ancient tree {variant} does not fit at ({ForestText.N(x)}, {ForestText.N(z)})");
        }
        List<Func<float, float, bool>> obstacles = [];
        foreach (CapSide side in sides)
        {
            obstacles.Add(side.Obstacle switch
            {
                Obstacle.Rockfall => Rockfall(d, side.Edge, Free),
                Obstacle.Trunk => Trunk(d, side.Edge, Free),
                Obstacle.Thicket => Thicket(d, side.Edge, Free, Clearing),
                _ => Brink(d, side, ravine!),
            });
        }
        bool OnObstacle(float x, float z) => obstacles.Any(o => o(x, z));

        // Young trees just beyond each thicket's clearing.
        foreach (CapSide side in sides.Where(s => s.Obstacle == Obstacle.Thicket))
        {
            int planted = 0;
            foreach ((float u, float v) in new[] { (9.6f, 12.4f), (21f, 12.2f), (4.8f, 10.6f), (25.2f, 10.2f), (15.6f, 13.6f) })
            {
                if (planted == youngTrees) break;
                (float x, float z) = side.Edge.At(u + d.Uniform(-0.6f, 0.6f), v + d.Uniform(-0.6f, 0.6f));
                if (Clearing(x, z) || !Free(x, z)) continue;
                TreeSpecies species = ForestDresser.PickSpecies(mix, d.Rng);
                if (species == TreeSpecies.TwistedTree) species = TreeSpecies.CommonTree;
                if (d.TryTree(species, x, z, d.Uniform(0.8f, 0.95f))) planted++;
            }
        }

        // The band all round, its trees off the clearings, the obstacles and the ravine (and its line to the camera).
        bool Brinkward(float x, float z, float keep) => InRavine(x, z) || RimDistance(x, z) < keep;
        d.BarrierBand(mix, s_sides, (x, z) => OnPath(x, z, 0.4f) || Brinkward(x, z, 1.8f),
            (x, z) => Clearing(x, z) || OnObstacle(x, z) || Brinkward(x, z, 4.5f) || InView(x, z, (px, pz) => Brinkward(px, pz, 1f)));
        if (ravine is not null)
        {
            d.Scatter([("Fern_1", 5), ("Grass_Wispy_Tall", 4), ("Plant_1", 2), ("avalon_hedge_dark", 1), ("Grass_Common_Tall", 2)], 26, 0.8f, 1.2f,
                (x, z) => Free(x, z) && RimDistance(x, z) < 3.5f);
        }
        d.Understorey(40, (x, z) => Free(x, z) && EdgeDistance(x, z) > 3.5f && !OnObstacle(x, z));
        d.MossyRocks(3, (x, z) => Free(x, z) && EdgeDistance(x, z) > 6f && !OnObstacle(x, z));
        d.GroundCover(55, Free);
        return new(name, terrain, d.Items)
        {
            Kind = SceneryKind.Cap,
            Family = family,
            Weight = 1f,
            Capped = [.. sides.Select(s => s.Side)],
            Kit = DecorJsonWriter.ForestKit,
        };
    }

    /// <summary>
    /// The exit's path carried in from the edge's midpoint: <paramref name="segments" /> dirt segments straight in, each
    /// starting <see cref="ForestPaths.Overlap" /> back over the last, their heights alternating as a chunk's do, on
    /// ground the cap keeps at y 0.
    /// </summary>
    private static void Path(SceneryDresser d, CapEdge e, int segments)
    {
        for (int k = 0; k < segments; k++)
        {
            (float x, float z) = e.At(Half, ForestPaths.SegmentLength / 2f + ForestPaths.Advance * k);
            if (!d.TryAddAt(ForestPaths.Segment, x, k % 2 * ForestPaths.Lift, z, e.Turn(0), 1f))
                throw new InvalidOperationException($"{d.Name}: the path's segment at ({ForestText.N(x)}, {ForestText.N(z)}) crosses the cell's edge");
        }
    }

    /// <summary>
    /// A rockfall across the edge band (<see cref="s_rockfall" />), about 11 m across and 4.5 m high over the path's end:
    /// scree of pebbles, small rocks and mossy stones spilling from its foot, ferns and bushes between its stones, and
    /// smaller rocks, boulders, bushes and brambles closing the rest of the edge. Returns where the heap stands.
    /// </summary>
    private static Func<float, float, bool> Rockfall(SceneryDresser d, CapEdge e, Func<float, float, bool> free)
    {
        foreach ((string model, float u, float v, int yaw, float scale, float lift) in s_rockfall)
        {
            (float x, float z) = e.At(u, v);
            float y = lift > 0f ? d.Height(x, z) + lift : d.Height(x, z) - 0.25f * scale;
            if (!d.TryAddAt(model, x, y, z, e.Turn(yaw), scale))
                throw new InvalidOperationException($"{d.Name}: the rockfall's {model} at ({ForestText.N(x)}, {ForestText.N(z)}) crosses the cell's edge");
            d.Take(x, z, d.Spacing(model, scale, 0.4f));
        }

        for (int i = 0; i < 80; i++)
        {
            float r = d.Uniform(0f, 1f);
            (float x, float z) = e.At(Half + d.Uniform(-7.5f, 7.5f), 4.8f - 4.2f * r * r);
            double roll = d.Rng.NextDouble();
            if (!free(x, z)) continue;
            if (roll < 0.5)
            {
                float scale = d.Uniform(1.3f, 2.4f);
                d.TryPlace(d.Pick(s_scree), x, z, 0.22f * scale, scale, 0.02f);
            }
            else if (roll < 0.6)
            {
                float scale = d.Uniform(0.22f, 0.4f);
                d.TryPlace(d.Pick(["Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3"]), x, z, 1.4f * scale, scale, 0.08f);
            }
            else
            {
                float scale = d.Uniform(0.35f, 0.85f);
                d.TryPlace(d.Pick(s_mossyRocks), x, z, 0.9f * scale, scale, 0.05f);
            }
        }

        (string Model, int Weight)[] flank =
        [
            ("Rock_Medium_1", 1), ("Rock_Medium_3", 1), ("avalon_boulder_1", 3), ("avalon_boulder_2", 2), ("avalon_rock_mossy_1", 2), ("avalon_rock_mossy_2", 2),
            ("Bush_Common_Flowers", 2), ("avalon_bramble_1", 2), ("Fern_1", 3), ("Plant_1_Big", 2),
        ];
        for (int row = 0; row < 2; row++)
        {
            for (float u = 0.9f + row * 1.1f; u < 29.2f; u += d.Uniform(1.7f, 2.5f))
            {
                if (u is > 9.2f and < 20.8f) continue;
                (float x, float z) = e.At(u, row == 0 ? d.Uniform(1f, 3.4f) : d.Uniform(3.6f, 6.4f));
                if (!free(x, z)) continue;
                string model = d.PickWeighted(flank);
                bool rock = model.StartsWith("Rock_", StringComparison.Ordinal) || model.StartsWith("avalon_boulder_", StringComparison.Ordinal)
                    || model.StartsWith("avalon_rock_", StringComparison.Ordinal);
                float scale = model.StartsWith("Rock_", StringComparison.Ordinal) ? d.Uniform(0.5f, 0.85f) : rock ? d.Uniform(0.8f, 1.05f) : d.Uniform(0.95f, 1.35f);
                d.TryPlace(model, x, z, d.Spacing(model, scale, 0.35f), scale, rock ? 0.18f * scale : 0f, yaw: e.Turn(d.Rng.Next(360)));
            }
        }

        bool Heap(float x, float z)
        {
            (float u, float v) = e.Of(x, z);
            return u is > 6.5f and < 24f && v is > 1f and < 12.5f;
        }
        d.Scatter([("Fern_1", 5), ("Bush_Common_Flowers", 2), ("Plant_1_Big", 2), ("Grass_Wispy_Tall", 3), ("avalon_hedge_dark", 1)], 24, 0.8f, 1.2f,
            (x, z) => Heap(x, z) && free(x, z));
        return Heap;
    }

    /// <summary>
    /// A giant fallen tree lying across the edge band and over the path, its root plate on edge behind its root end over
    /// the torn earth, stones thrown out of the hollow, ferns crowding its flanks; fallen logs and brambles closing the
    /// rest of the edge on both sides. Returns where the giant and its plate lie.
    /// </summary>
    private static Func<float, float, bool> Trunk(SceneryDresser d, CapEdge e, Func<float, float, bool> free)
    {
        TrunkLayout plan = TrunkPlan();
        (float gx, float gz) = e.At(plan.Middle.U, plan.Middle.V);
        (float px, float pz) = e.At(plan.Plate.U, plan.Plate.V);
        (float hx, float hz) = e.At(plan.Hollow.U, plan.Hollow.V);
        (float X, float Z) root = e.At(plan.Root.U, plan.Root.V), crown = e.At(plan.Crown.U, plan.Crown.V);
        int yaw = e.Turn(TrunkYaw);
        if (!d.TryAddAt("avalon_giant_fallen_1", gx, 0f, gz, yaw, 1f) || !d.TryAddAt("avalon_root_plate_1", px, d.Height(px, pz), pz, yaw, 1f))
            throw new InvalidOperationException($"{d.Name}: the giant or its root plate crosses the cell's edge");
        float Along(float x, float z) => SegmentDistance(x, z, root, crown);
        for (float t = 0f; t <= 1.001f; t += 0.1f) d.Take(root.X + (crown.X - root.X) * t, root.Z + (crown.Z - root.Z) * t, 1.6f);
        d.Take(px, pz, 2.4f);

        for (int i = 0; i < 8; i++)
        {
            (float s, float c) = Yaw.Of(d.Rng.Next(360));
            float r = d.Uniform(0.4f, 2.2f);
            string model = i < 5 ? d.Pick(s_scree) : d.Pick(["avalon_rock_mossy_3", "Rock_Medium_2"]);
            float scale = i < 5 ? d.Uniform(1.4f, 2.6f) : model == "Rock_Medium_2" ? d.Uniform(0.22f, 0.35f) : d.Uniform(0.45f, 0.65f);
            d.TryPlace(model, hx + c * r, hz + s * r, 0.3f, scale, 0.03f);
        }

        // East of the plate and west of the broken crown: logs crossed and brambles to the corners.
        (float l1x, float l1z) = e.At(25.2f, 2.4f);
        Log(d, "avalon_log_fallen_1", l1x, l1z, e.Turn(172), 0.95f);
        (float l2x, float l2z) = e.At(27.1f, 4.7f);
        Log(d, "avalon_log_fallen_2", l2x, l2z, e.Turn(100), 0.9f);
        (float b1x, float b1z) = e.At(23.6f, 4.4f);
        Cluster(d, b1x, b1z, ["avalon_bramble_2", "avalon_bramble_1", "avalon_hedge_dark"], 1.2f, 0.95f, 1.2f);
        (float b2x, float b2z) = e.At(2.4f, 3.8f);
        Cluster(d, b2x, b2z, ["avalon_bramble_1", "avalon_bramble_2", "Fern_1"], 1.3f, 0.9f, 1.15f);

        d.Scatter([("Fern_1", 6), ("Plant_1_Big", 2), ("Grass_Wispy_Tall", 2), ("Mushroom_Laetiporus", 1)], 26, 0.85f, 1.3f,
            (x, z) => Along(x, z) is > 1.9f and < 4.2f && free(x, z));
        return (x, z) => Along(x, z) < 3.2f || SceneryDresser.Distance(x, z, px, pz) < 4.5f;
    }

    /// <summary>
    /// A thicket across the edge band: brambles, dark hedges and flowering bushes 6.4-9.6 m deep, ferns and big plants in
    /// front, closing in on the path and arching over its end, a fallen log in it on either side and dead snags standing
    /// out of it beyond the clearing. Returns where it stands.
    /// </summary>
    private static Func<float, float, bool> Thicket(SceneryDresser d, CapEdge e, Func<float, float, bool> free, Func<float, float, bool> clearing)
    {
        foreach ((string model, float u, float v, int yaw, float scale) in new[]
        {
            ("avalon_bramble_2", 15.1f, 5.3f, 30, 1.3f), ("avalon_bramble_1", 13.6f, 6.6f, 200, 1.2f), ("avalon_hedge_dark", 16.9f, 6.3f, 0, 1.3f),
            ("avalon_bramble_1", 16.6f, 4.5f, 110, 1.05f), ("avalon_bramble_2", 12.7f, 4.6f, 300, 1.1f),
        })
        {
            (float x, float z) = e.At(u, v);
            d.TryPlace(model, x, z, d.Spacing(model, scale, 0.25f), scale, yaw: e.Turn(yaw));
        }
        (float l1x, float l1z) = e.At(7.4f, 5f);
        Log(d, "avalon_log_fallen_1", l1x, l1z, e.Turn(96), 1f);
        (float l2x, float l2z) = e.At(23.4f, 6.2f);
        Log(d, "avalon_log_fallen_2", l2x, l2z, e.Turn(64), 1f);

        (string Model, int Weight)[] front = [("Fern_1", 4), ("Plant_1_Big", 3), ("avalon_hedge_dark", 2), ("avalon_bramble_1", 2), ("Grass_Wispy_Tall", 2)];
        (string Model, int Weight)[] middle = [("avalon_bramble_1", 3), ("avalon_bramble_2", 3), ("avalon_hedge_dark", 2), ("Bush_Common_Flowers", 1), ("Plant_1_Big", 1)];
        (string Model, int Weight)[] back = [("avalon_bramble_2", 3), ("avalon_hedge_dark", 2), ("avalon_bramble_1", 2), ("Bush_Common_Flowers", 1)];
        // Its back edge wanders 6.4-9.6 m in, so it reads as grown, not planted.
        int seed = ForestDresser.StableSeed(d.Name) + (int)e.Side;
        float Depth(float u) => 6.4f + 3.2f * Noise(seed, u, 0f, 7f);
        for (int row = 0; row < 7; row++)
        {
            for (float u = 0.7f + d.Uniform(0f, 1f); u < 29.3f; u += d.Uniform(1.4f, 2.1f))
            {
                float v = 1f + row * 1.4f + d.Uniform(-0.5f, 0.5f);
                if (row > 0 && v > Depth(u)) continue;
                (float x, float z) = e.At(u, v);
                if (!free(x, z)) continue;
                string model = d.PickWeighted(row == 0 ? front : row < 3 ? middle : back);
                float scale = row == 0 ? d.Uniform(0.85f, 1.2f) : row < 3 ? d.Uniform(1f, 1.3f) : d.Uniform(1.1f, 1.45f);
                d.TryPlace(model, x, z, d.Spacing(model, scale, 0.2f), scale, yaw: e.Turn(d.Rng.Next(360)));
            }
        }
        // Dead snags standing out of it beyond the clearing.
        foreach ((float u, float v) in new[] { (4f, 6.6f), (26f, 7.2f) })
        {
            (float x, float z) = e.At(u, v);
            if (free(x, z) && !clearing(x, z)) d.TryTree(TreeSpecies.DeadTree, x, z, d.Uniform(0.8f, 0.95f));
        }
        return (x, z) => e.Of(x, z).V < Depth(e.Of(x, z).U) + 1.2f;
    }

    /// <summary>
    /// The path breaking off at a ravine's brink 2 m in: the ravine (its walls and mist are the terrain's) with its lip
    /// all round, a log rail lying across the path's end on the brink, a broken fence along the brink on either side of
    /// it, set back from the drop, and the dead trees on the floor. Returns where the ravine and its rim lie.
    /// </summary>
    private static Func<float, float, bool> Brink(SceneryDresser d, CapSide side, Outline ravine)
    {
        CapEdge e = side.Edge;
        Lips(d, ravine);

        (float rx, float rz) = e.At(15.3f, 1.65f);
        if (!d.TryAddAt("avalon_log_fallen_2", rx, d.Height(rx, rz) - 0.06f, rz, e.Turn(4), 1f))
            throw new InvalidOperationException($"{d.Name}: the log rail crosses the cell's edge");
        d.Take(rx, rz, 1.4f);

        // The fences: along the brink's sides either side of the one the path meets, 1.1 m back from the wall.
        Outline brink = side.Ravine!;
        int meet = Enumerable.Range(0, brink.Count).Where(i =>
        {
            (float X, float Z) a = brink[i], b = brink[(i + 1) % brink.Count];
            return MathF.Min(a.X, b.X) <= Half && MathF.Max(a.X, b.X) >= Half;
        }).MinBy(i => brink[i].Z + brink[(i + 1) % brink.Count].Z);
        foreach (int i in new[] { (meet + brink.Count - 1) % brink.Count, (meet + 1) % brink.Count })
        {
            (float X, float Z) a = brink[i], b = brink[(i + 1) % brink.Count];
            float length = SceneryDresser.Distance(a.X, a.Z, b.X, b.Z);
            (float tu, float tv) = ((b.X - a.X) / length, (b.Z - a.Z) / length);
            // (tv, -tu) looks into the ravine: the fence stands back from it.
            (float x, float z) = e.At((a.X + b.X) / 2f - tv * 1.1f, (a.Z + b.Z) / 2f + tu * 1.1f);
            string model = length > 5.5f ? "avalon_fence_broken_1" : "avalon_fence_broken_2";
            if (d.TryAddAt(model, x, d.Height(x, z) - 0.02f, z, e.Turn(SceneryDresser.YawToward(-tv, tu) + d.Rng.Next(-4, 5)), 1f))
                d.Take(x, z, 1f);
        }

        foreach ((string model, float u, float v, float scale) in side.Dead ?? [])
        {
            (float x, float z) = e.At(u, v);
            if (!d.TryAdd(model, x, z, d.Rng.Next(360), scale, sink: 0.3f))
                throw new InvalidOperationException($"{d.Name}: dead tree {model} does not fit at ({ForestText.N(x)}, {ForestText.N(z)})");
        }
        return (x, z) => Inside(ravine, x, z) || OutlineDistance(ravine, x, z) < 1.8f;
    }

    // ---- shared dressing ----

    /// <summary>
    /// The floor inside the band: a few kit saplings or snags of the mix (kept under any ancient crown), the darker
    /// understorey, mossy stones and ground cover, none where <paramref name="clear" /> holds, and no sapling where
    /// <paramref name="treesClear" /> does (a feature's line to the camera).
    /// </summary>
    private static void Inner(
        SceneryDresser d, TreeMix mix, int saplings, int understorey, int cover, int rocks, Func<float, float, bool>? clear = null,
        Func<float, float, bool>? treesClear = null)
    {
        clear ??= static (_, _) => false;
        treesClear ??= static (_, _) => false;
        for (int planted = 0, attempts = 0; planted < saplings && attempts < 200; attempts++)
        {
            float x = d.Uniform(7f, 23f), z = d.Uniform(7f, 23f);
            if (clear(x, z) || treesClear(x, z)) continue;
            TreeSpecies species = ForestDresser.PickSpecies(mix, d.Rng);
            if (species == TreeSpecies.TwistedTree) species = TreeSpecies.DeadTree;
            if (d.TryTree(species, x, z, d.Uniform(0.85f, 1.15f))) planted++;
        }
        d.Understorey(understorey, (x, z) => EdgeDistance(x, z) > 3.5f && !clear(x, z));
        d.MossyRocks(rocks, (x, z) => EdgeDistance(x, z) > 5f && !clear(x, z));
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
    /// A loose cluster of the models about a point, each at a scale in the range, within <paramref name="spread" />.
    /// </summary>
    private static void Cluster(SceneryDresser d, float x, float z, string[] models, float spread, float minScale, float maxScale, float keep = 0.5f)
    {
        foreach (string model in models)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float scale = d.Uniform(minScale, maxScale);
                if (d.TryPlace(model, x + d.Uniform(-spread, spread), z + d.Uniform(-spread, spread), d.Spacing(model, scale, keep), scale, 0.06f)) break;
            }
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
        Cluster(d, x - 1.5f, z - 2f, ["avalon_rock_mossy_1", "avalon_rock_mossy_3"], 1.2f, 0.6f, 0.95f);
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
