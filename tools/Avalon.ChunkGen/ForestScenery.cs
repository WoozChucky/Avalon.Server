using Avalon.World.ChunkLayouts;
using Outline = System.Collections.Generic.IReadOnlyList<(float X, float Z)>;

namespace Avalon.ChunkGen;

/// <summary>
/// The forest's scenery pieces (map 2, scenery ring #1110): what the ring generator draws on the cells around a
/// generated layout. Every piece is drawn from the forest kit (the darkened nature kit and the custom avalon_* forest
/// models), meets every edge at y 0 on forest floor, carries a barrier band on all four sides (SceneryDresser) and
/// keeps its feature inside the band, so any piece sits beside any other or any chunk at any rotation. A border's front
/// is S: the ring generator turns it toward the layout. The borders:
/// <list type="bullet">
/// <item>old woods (the filler, four variants): two or three ancient trees, a darker understorey, mossy stones, fallen
/// wood, gentle relief;</item>
/// <item>mossy cliffs (two): a plateau inside the band, its rough rock face dressed with layered face sections, ledges,
/// roots and creepers over the lip, scree at the foot, trees on top;</item>
/// <item>ravine into mist (two): an irregular cut down to <see cref="RavineFloor" />, rock walls, an overhanging lip
/// all round its brink, dead trees rising out of three mist layers;</item>
/// <item>giant fallen tree (one): the giant lying across the cell, its root plate on edge at its root end over the torn
/// earth it left;</item>
/// <item>dark stream (two): water rising among mossy stones and running down its bed into a still pool or away among
/// stones, stones and pebbles along its banks.</item>
/// </list>
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
    private const float MaxOverlap = 3f;

    /// <summary>
    /// A face section's foot stands this far out from the plateau's rough face, which stands this far out from the
    /// plateau's top.
    /// </summary>
    private const float FaceToWall = 2.2f, WallToTop = 0.4f;

    /// <summary>The scale range a cliff face section is drawn at.</summary>
    private const float FaceMinScale = 0.85f, FaceMaxScale = 1.25f;

    private static readonly string[] s_scree = ["Pebble_Round_1", "Pebble_Round_3", "Pebble_Round_5", "Pebble_Round_2"];

    private static readonly string[] s_mossyRocks = ["avalon_rock_mossy_1", "avalon_rock_mossy_2", "avalon_rock_mossy_3"];

    /// <summary>Every forest scenery piece: the eleven borders.</summary>
    public static IReadOnlyList<SceneryPiece> Pieces()
    {
        IReadOnlyDictionary<string, ModelBounds> kit = KitBounds.Load();
        return
        [
            Woods(1, kit, TreeMix.Old, [(1, 9.5f, 10.5f), (4, 20.5f, 12f), (3, 13.5f, 20.5f)], WoodsLogAndStones),
            Woods(2, kit, TreeMix.Mixed, [(2, 10.5f, 18.5f), (3, 19.5f, 10f)], WoodsOutcrop),
            Woods(3, kit, TreeMix.Pine, [(4, 10f, 9.5f), (1, 20.5f, 19f), (2, 10.5f, 20.5f)], WoodsDeadfall),
            Woods(4, kit, TreeMix.Broadleaf, [(2, 9f, 13f), (4, 20f, 18.5f)], WoodsThicket),
            Cliffs(1, kit, 7f, TreeMix.Mixed, [(7f, 9.5f), (11.5f, 6.4f), (19.5f, 6.8f), (23.6f, 11f), (23.2f, 19.5f), (17.5f, 23.6f), (8.5f, 22f), (6.3f, 16f)], ancientOnTop: true),
            Cliffs(2, kit, 7.8f, TreeMix.Pine, [(6.4f, 12f), (9.5f, 6.6f), (17f, 6.2f), (23.4f, 9.8f), (23.8f, 17.5f), (20f, 23.4f), (11f, 23.6f), (6.2f, 19f)], ancientOnTop: false),
            Ravine(1, kit, TreeMix.Old,
                [(12.5f, 24.2f), (18.5f, 23.6f), (23.2f, 20.5f), (24.4f, 15f), (22.6f, 9.6f), (18f, 6.2f), (12f, 5.8f), (7.5f, 8.6f), (5.8f, 13.8f), (6.6f, 19.5f), (9f, 22.8f)],
                [("DeadTree_5", 14.5f, 16f, 1.15f), ("DeadTree_2", 18.5f, 11.5f, 1.5f)]),
            Ravine(2, kit, TreeMix.Mixed,
                [(5.6f, 11.5f), (8.5f, 15.2f), (12f, 17f), (15.5f, 20.2f), (19.5f, 22f), (23f, 24.2f), (24.6f, 21f), (23.8f, 16.5f), (19.8f, 14.8f), (16.5f, 11.6f), (12.5f, 9.8f), (9f, 6f), (6f, 7.2f)],
                [("DeadTree_5", 11.5f, 12.8f, 1.05f), ("DeadTree_2", 18f, 17.8f, 1.4f)]),
            Giant(kit),
            Stream(1, kit, TreeMix.Broadleaf,
                [(22.3f, 17f), (19.6f, 18f), (17f, 16.6f), (15.6f, 14f), (13.4f, 12.6f), (11.2f, 13f)], pool: (9.4f, 13.6f, 2.3f),
                ancient: (2, 14.5f, 22.5f)),
            Stream(2, kit, TreeMix.Mixed,
                [(21.4f, 21f), (18.9f, 21.8f), (16.2f, 21.2f), (13.9f, 19.4f), (12.7f, 16.6f), (12.8f, 13.6f), (11.7f, 11.1f), (10f, 9.5f)], pool: null,
                ancient: null),
        ];
    }

    // ---- old woods ----

    private static SceneryPiece Woods(
        int n, IReadOnlyDictionary<string, ModelBounds> kit, TreeMix mix, (int Variant, float X, float Z)[] ancients, Action<SceneryDresser> feature)
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
        d.BarrierBand(mix, s_sides);
        Inner(d, mix, saplings: 1, understorey: 80, cover: 60, rocks: 5);
        return Border(name, "woods", 3f, terrain, d, filler: true);
    }

    /// <summary>
    /// A long fallen log across the middle, mushrooms on it, mossy stones in a cluster and a bramble patch.
    /// </summary>
    private static void WoodsLogAndStones(SceneryDresser d)
    {
        Log(d, "avalon_log_fallen_1", 15.5f, 15f, 68, 1.35f);
        Log(d, "avalon_log_fallen_2", 19.2f, 22.4f, 152, 1.1f);
        Cluster(d, 21.5f, 20.5f, ["avalon_rock_mossy_1", "avalon_rock_mossy_2", "avalon_rock_mossy_3", "avalon_rock_mossy_2"], 2.6f, 0.8f, 1.25f);
        Cluster(d, 8f, 17f, ["avalon_bramble_2", "avalon_bramble_1", "avalon_bramble_1"], 2.2f, 0.9f, 1.15f);
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

    /// <summary>Two fallen logs crossed, a line of mossy stones and a dead snag among the ferns.</summary>
    private static void WoodsDeadfall(SceneryDresser d)
    {
        Log(d, "avalon_log_fallen_1", 19.5f, 9f, 115, 1.25f);
        Log(d, "avalon_log_fallen_2", 21f, 10.5f, 35, 1.1f);
        for (int i = 0; i < 4; i++)
            d.TryPlace(d.Pick(["avalon_rock_mossy_1", "avalon_rock_mossy_2", "avalon_rock_mossy_3"]), 14f + i * 1.9f, 14.5f - i * 0.5f + d.Uniform(-0.4f, 0.4f), 0.8f, d.Uniform(0.75f, 1.2f), 0.05f);
        d.TryTree(TreeSpecies.DeadTree, 15.5f, 21.5f, 1.05f);
    }

    /// <summary>A mossy boulder under a creeper, a thicket of brambles and dark hedges, a log.</summary>
    private static void WoodsThicket(SceneryDresser d)
    {
        if (!d.TryPlace("avalon_boulder_3", 19.5f, 9f, 2f, 1f, 0.2f, yaw: 205))
            throw new InvalidOperationException($"{d.Name}: the boulder does not fit at (19.5, 9)");
        HangOver(d, "avalon_creeper_1", 19.5f, 9f, 1.46f, 205, top: 2.41f - 0.3f);
        Cluster(d, 21.5f, 11.5f, ["avalon_rock_mossy_2", "avalon_rock_mossy_1"], 1.4f, 0.7f, 1f);
        Cluster(d, 10f, 21.5f, ["avalon_bramble_2", "avalon_hedge_dark", "avalon_bramble_1", "avalon_hedge_dark", "avalon_bramble_2"], 2.8f, 0.9f, 1.25f);
        Log(d, "avalon_log_fallen_1", 14.5f, 9.5f, 160, 1.2f);
    }

    // ---- mossy cliffs ----

    private static SceneryPiece Cliffs(int n, IReadOnlyDictionary<string, ModelBounds> kit, float height, TreeMix mix, Outline foot, bool ancientOnTop)
    {
        string name = $"forest_ring_cliffs_{n:00}";
        Convex(name, foot);
        Outline wall = Inset(foot, FaceToWall);
        Outline top = Inset(foot, FaceToWall + WallToTop);
        int seed = ForestDresser.StableSeed(name);

        float Height(float x, float z)
        {
            if (Inside(top, x, z)) return height;
            float beyond = OutlineDistance(top, x, z);
            if (beyond < 1f) return height * (1f - beyond);
            return Relief(seed, x, z) * Smooth(FaceToWall + WallToTop + 1f, FaceToWall + WallToTop + 4f, beyond);
        }

        var terrain = new SceneryTerrain(Height, (x, z) => Height(x, z) > 0.2f && Height(x, z) < height - 0.05f ? Rock : Floor)
        {
            Walls = [new SceneryWall(wall, Closed: true, Bottom: -0.6f, Top: height, Rock)],
        };
        var d = new SceneryDresser(name, terrain, kit);

        // The face: layered sections end to end along each side of the foot, with ledges, roots and creepers.
        for (int i = 0; i < foot.Count; i++)
            FaceSections(d, foot[i], foot[(i + 1) % foot.Count], height);

        // On top: an ancient tree or a stand of kit trees, undergrowth to the lip.
        bool OnTop(float x, float z, float margin) => Inside(top, x, z) && OutlineDistance(top, x, z) >= margin;
        (float cx, float cz) = Centroid(top);
        if (ancientOnTop && !d.TryAncient(1 + d.Rng.Next(4), cx, cz, d.Rng.Next(360)))
            throw new InvalidOperationException($"{name}: no ancient tree fits on top at ({ForestText.N(cx)}, {ForestText.N(cz)})");
        for (int planted = 0, attempts = 0; planted < (ancientOnTop ? 3 : 6) && attempts < 300; attempts++)
        {
            float x = d.Uniform(0f, Cell), z = d.Uniform(0f, Cell);
            if (!OnTop(x, z, 1.4f)) continue;
            TreeSpecies species = ForestDresser.PickSpecies(mix, d.Rng);
            if (species == TreeSpecies.TwistedTree) species = TreeSpecies.Pine;
            if (d.TryTree(species, x, z, d.Uniform(0.85f, 1.25f))) planted++;
        }
        d.Understorey(ancientOnTop ? 24 : 30, (x, z) => OnTop(x, z, 0.7f), 0.7f, 1.2f);
        d.GroundCover(20, (x, z) => OnTop(x, z, 0.4f));

        // At the foot: scree of pebbles and small rocks, ferns among it.
        for (int i = 0; i < foot.Count; i++)
            Scree(d, foot[i], foot[(i + 1) % foot.Count]);

        bool Off(float x, float z, float keep) => !Inside(foot, x, z) && OutlineDistance(foot, x, z) >= keep;
        d.BarrierBand(mix, s_sides, (x, z) => !Off(x, z, 2.8f));
        d.Understorey(26, (x, z) => Off(x, z, 0.6f) && EdgeDistance(x, z) > 3f);
        d.GroundCover(30, (x, z) => Off(x, z, 0.3f));
        return Border(name, "cliffs", 1f, terrain, d);
    }

    /// <summary>
    /// Face sections along a side of the foot (from <paramref name="a" /> to <paramref name="b" />, the outline running
    /// counter-clockwise so the face looks out), end to end with at least <see cref="SectionOverlap" />, reaching a
    /// little past each corner; each scaled so its rock stands at least the plateau's height and sunk so its top meets
    /// the plateau. The chain of sections is the one whose scales come nearest 1.
    /// </summary>
    private static void FaceSections(SceneryDresser d, (float X, float Z) a, (float X, float Z) b, float height)
    {
        float length = SceneryDresser.Distance(a.X, a.Z, b.X, b.Z);
        (float tx, float tz) = ((b.X - a.X) / length, (b.Z - a.Z) / length);
        (float nx, float nz) = (tz, -tx);                       // outward
        int yaw = SceneryDresser.YawToward(nx, nz);
        (float ax, float az) = Yaw.Turn(1f, 0f, yaw);            // the section's own +X
        float target = length + 0.7f;

        List<int> best = [];
        float bestScore = float.MaxValue, bestFactor = 1f, bestOverlap = SectionOverlap;
        for (int k = 1; k <= 3; k++)
        {
            foreach (int[] chain in Chains(k))
            {
                // At their least scales the sections may be longer than the side: they overlap more, up to MaxOverlap.
                float least = chain.Sum(c => s_faces[c].Length * MinFaceScale(c, height));
                float overlap = k == 1 ? 0f : MathF.Max(SectionOverlap, (least - target) / (k - 1));
                if (overlap > MaxOverlap) continue;
                float factor = (target + overlap * (k - 1)) / least;
                if (factor < 1f - 0.001f || chain.Any(c => MinFaceScale(c, height) * factor > FaceMaxScale)) continue;
                float score = chain.Sum(c => MathF.Abs(MinFaceScale(c, height) * factor - 1.05f)) / k + 0.08f * k
                    + 0.1f * (overlap - SectionOverlap) + 0.15f * Repeats(chain) + d.Uniform(0f, 0.12f);
                if (score < bestScore) (best, bestScore, bestFactor, bestOverlap) = ([.. chain], score, factor, overlap);
            }
        }
        if (best.Count == 0)
            throw new InvalidOperationException($"{d.Name}: no chain of face sections spans the {ForestText.N(length)} m side from ({ForestText.N(a.X)}, {ForestText.N(a.Z)})");

        float[] scales = [.. best.Select(c => SceneryDresser.Round(MinFaceScale(c, height) * bestFactor))];
        float total = best.Select((c, i) => s_faces[c].Length * scales[i]).Sum() - bestOverlap * (best.Count - 1);
        float cursor = (length - total) / 2f;
        for (int i = 0; i < best.Count; i++)
        {
            (string model, float sectionLength, float rock, float bulge, float ends, float lean) = s_faces[best[i]];
            float scale = scales[i];
            float along = cursor + sectionLength * scale / 2f;
            cursor += sectionLength * scale - bestOverlap;
            float x = a.X + tx * along, z = a.Z + tz * along, y = height - rock * scale;
            if (!d.TryAddAt(model, x, y, z, yaw, scale))
                throw new InvalidOperationException($"{d.Name}: face section {model} at ({ForestText.N(x)}, {ForestText.N(z)}) crosses the cell's edge");

            // A point on the face at its own (u across -1..1, h up in its metres): its plan bulges, its ends turn back, it leans back.
            (float X, float Z) OnFace(float u, float h, float back)
            {
                float plan = bulge * (1f - u * u) - ends * MathF.Pow(Smooth(0.62f, 1f, MathF.Abs(u)), 1.4f) - lean * h / rock - back;
                float lx = u * sectionLength / 2f;
                return (x + (ax * lx + nx * plan) * scale, z + (az * lx + nz * plan) * scale);
            }

            // Ledges a third to two thirds up, one or two, apart.
            int ledges = d.Rng.NextDouble() < 0.8 ? 1 + (d.Rng.NextDouble() < 0.45 ? 1 : 0) : 0;
            float lastU = 9f;
            for (int l = 0; l < ledges; l++)
            {
                float u = d.Uniform(-0.45f, 0.45f);
                if (MathF.Abs(u - lastU) < 0.45f) u = -u;
                lastU = u;
                float h = rock * d.Uniform(0.3f, 0.68f);
                (float lx, float lz) = OnFace(u, h, 0f);
                d.TryAddAt(d.Pick(["avalon_cliff_ledge_1", "avalon_cliff_ledge_2"]), lx, y + h * scale, lz, yaw + d.Rng.Next(-6, 7), d.Uniform(0.85f, 1.1f));
            }

            // Roots gripping over the lip and hanging down the face; a creeper beside them.
            if (d.Rng.NextDouble() < 0.75)
            {
                float u = d.Uniform(-0.5f, 0.5f);
                (float rx, float rz) = OnFace(u, rock, 0.05f);
                d.TryAddAt("avalon_roots_hanging_1", rx, height, rz, yaw + d.Rng.Next(-10, 11), d.Uniform(0.9f, 1.15f));
                float v = u > 0f ? u - d.Uniform(0.35f, 0.6f) : u + d.Uniform(0.35f, 0.6f);
                if (d.Rng.NextDouble() < 0.55)
                {
                    (float cxx, float czz) = OnFace(v, rock, 0.05f);
                    d.TryAddAt("avalon_creeper_1", cxx, height, czz, yaw + d.Rng.Next(-8, 9), d.Uniform(0.9f, 1.15f));
                }
            }
        }
    }

    /// <summary>
    /// The least scale a face section is drawn at for a plateau of the height: its rock reaches the top.
    /// </summary>
    private static float MinFaceScale(int face, float height) => MathF.Max(FaceMinScale, height / s_faces[face].Height);

    /// <summary>Every sequence of <paramref name="k" /> face sections.</summary>
    private static IEnumerable<int[]> Chains(int k) =>
        k == 1 ? Enumerable.Range(0, s_faces.Length).Select(i => new[] { i }) : Chains(k - 1).SelectMany(shorter => Enumerable.Range(0, s_faces.Length).Select(i => (int[])[.. shorter, i]));

    /// <summary>How many times a section stands beside the same section in the chain.</summary>
    private static int Repeats(int[] chain) => Enumerable.Range(1, chain.Length - 1).Count(i => chain[i] == chain[i - 1]);

    /// <summary>
    /// Scree along a side of the foot: pebbles mostly, small rocks and mossy stones, thinning out from the face; ferns
    /// and grass among it.
    /// </summary>
    private static void Scree(SceneryDresser d, (float X, float Z) a, (float X, float Z) b)
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

        bool Mist(float x, float z) => Inside(brink, x, z) && OutlineDistance(brink, x, z) >= MistInset;
        var terrain = new SceneryTerrain(Height, (x, z) => Height(x, z) < -0.3f ? Rock : Floor)
        {
            Walls = [new SceneryWall(brink, Closed: true, Bottom: RavineFloor - 0.5f, Top: 0f, Rock)],
            Surfaces = [.. s_mistLayers.Select(y => ScenerySurface.Area(Mist, y, "mist"))],
        };
        var d = new SceneryDresser(name, terrain, kit);

        // The lip all round the brink: one section a side, its void toward the ravine, a few more roots and creepers under it.
        int last = -1;
        for (int i = 0; i < brink.Count; i++)
        {
            (float X, float Z) a = brink[i], b = brink[(i + 1) % brink.Count];
            float length = SceneryDresser.Distance(a.X, a.Z, b.X, b.Z);
            (float tx, float tz) = ((b.X - a.X) / length, (b.Z - a.Z) / length);
            (float nx, float nz) = (tz, -tx);                    // into the ravine
            int lip = Enumerable.Range(0, s_lips.Length).Where(l => l != last)
                .OrderBy(l => MathF.Abs((length + SectionOverlap) / s_lips[l].Length - 1f) + d.Uniform(0f, 0.08f)).First();
            last = lip;
            float scale = Math.Clamp((length + SectionOverlap) / s_lips[lip].Length, 0.8f, 1.25f);
            float mx = (a.X + b.X) / 2f, mz = (a.Z + b.Z) / 2f;
            int yaw = SceneryDresser.YawToward(nx, nz);
            if (!d.TryAddAt(s_lips[lip].Model, mx, 0f, mz, yaw, scale))
                throw new InvalidOperationException($"{name}: lip {s_lips[lip].Model} at ({ForestText.N(mx)}, {ForestText.N(mz)}) crosses the cell's edge");
            d.Take(mx - nx * 0.6f, mz - nz * 0.6f, 1.4f);
            if (d.Rng.NextDouble() < 0.45)
            {
                float along = d.Uniform(-0.3f, 0.3f) * length;
                d.TryAddAt(d.Rng.NextDouble() < 0.6 ? "avalon_roots_hanging_2" : "avalon_creeper_2",
                    mx + tx * along + nx * 0.8f, 0f, mz + tz * along + nz * 0.8f, yaw + d.Rng.Next(-12, 13), d.Uniform(0.9f, 1.2f));
            }
        }

        foreach ((string model, float x, float z, float scale) in dead)
        {
            if (!d.TryAdd(model, x, z, d.Rng.Next(360), scale, sink: 0.3f))
                throw new InvalidOperationException($"{name}: dead tree {model} does not fit at ({ForestText.N(x)}, {ForestText.N(z)})");
        }

        bool Ground(float x, float z, float keep) => !Inside(brink, x, z) && OutlineDistance(brink, x, z) >= keep;
        d.BarrierBand(mix, s_sides, (x, z) => !Ground(x, z, 1.8f), (x, z) => !Ground(x, z, 4.5f));
        // Along the brink: ferns, grass and the odd bush, as the spike's rim had; then the floor between it and the band.
        d.Scatter([("Fern_1", 5), ("Grass_Wispy_Tall", 4), ("Plant_1", 2), ("avalon_hedge_dark", 1), ("Grass_Common_Tall", 2)], 34, 0.8f, 1.2f,
            (x, z) => Ground(x, z, 1.5f) && OutlineDistance(brink, x, z) < 3.5f);
        d.Understorey(24, (x, z) => Ground(x, z, 2.5f) && EdgeDistance(x, z) > 3f);
        d.GroundCover(30, (x, z) => Ground(x, z, 1.4f));
        return Border(name, "ravine", 1f, terrain, d);
    }

    // ---- giant fallen tree ----

    private static SceneryPiece Giant(IReadOnlyDictionary<string, ModelBounds> kit)
    {
        const string Name = "forest_ring_giant_01", Model = "avalon_giant_fallen_1";
        const int GiantYaw = 135;                               // its +X, root end to crown end, runs south-west
        const float HalfLength = 9.1f, PlateBack = 0.25f, PlateFromCentre = 6.8f;
        int seed = ForestDresser.StableSeed(Name);
        (float dx, float dz) = Yaw.Turn(1f, 0f, GiantYaw);
        // The plate stands 6.8 m from the cell's centre along the diagonal, the giant running back across the centre.
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
        if (!d.TryAncient(3, 8.4f, 21.6f, d.Rng.Next(360)))
            throw new InvalidOperationException($"{Name}: the ancient tree does not fit");
        d.TryTree(TreeSpecies.DeadTree, 21.5f, 8.6f, 0.9f);
        d.TryTree(TreeSpecies.Pine, 18.6f, 7.4f, 0.95f);
        Log(d, "avalon_log_fallen_2", 21.5f, 12.8f, 80, 0.9f);

        d.BarrierBand(TreeMix.Mixed, s_sides, (x, z) => Trunk(x, z) < 3.2f || SceneryDresser.Distance(x, z, px, pz) < 4.5f);
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
        const float HalfWidth = 0.85f, Top = -0.4f, Slope = 0.012f;

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

        d.BarrierBand(mix, s_sides, (x, z) => Wet(x, z) || MathF.Min(Bed(x, z), PoolDistance(x, z)) < HalfWidth + 2f,
            (x, z) => MathF.Min(Bed(x, z), PoolDistance(x, z)) < HalfWidth + 4.5f);
        Inner(d, mix, saplings: ancient is null ? 2 : 0, understorey: 40, cover: 50, rocks: 3, clear: (x, z) => MathF.Min(Bed(x, z), PoolDistance(x, z)) < HalfWidth + 1.4f);
        return Border(name, "stream", 1f, terrain, d);
    }

    // ---- shared dressing ----

    /// <summary>
    /// The floor inside the band: a few kit saplings or snags of the mix (kept under any ancient crown), the darker
    /// understorey, mossy stones and ground cover, none where <paramref name="clear" /> holds.
    /// </summary>
    private static void Inner(SceneryDresser d, TreeMix mix, int saplings, int understorey, int cover, int rocks, Func<float, float, bool>? clear = null)
    {
        clear ??= static (_, _) => false;
        for (int planted = 0, attempts = 0; planted < saplings && attempts < 200; attempts++)
        {
            float x = d.Uniform(7f, 23f), z = d.Uniform(7f, 23f);
            if (clear(x, z)) continue;
            TreeSpecies species = ForestDresser.PickSpecies(mix, d.Rng);
            if (species == TreeSpecies.TwistedTree) species = TreeSpecies.DeadTree;
            if (d.TryTree(species, x, z, d.Uniform(0.85f, 1.15f))) planted++;
        }
        d.Understorey(understorey, (x, z) => EdgeDistance(x, z) > 3.5f && !clear(x, z));
        d.MossyRocks(rocks, (x, z) => EdgeDistance(x, z) > 5f && !clear(x, z));
        d.GroundCover(cover, (x, z) => !clear(x, z));
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

    /// <summary>
    /// Value noise 0-1 on a lattice of the cell size: hashed corners, smoothstep between them (no trigonometry, so
    /// every machine agrees).
    /// </summary>
    private static float Noise(int seed, float x, float z, float size)
    {
        float fx = x / size, fz = z / size;
        int ix = (int)MathF.Floor(fx), iz = (int)MathF.Floor(fz);
        float tx = fx - ix, tz = fz - iz;
        tx = tx * tx * (3f - 2f * tx);
        tz = tz * tz * (3f - 2f * tz);
        float a = Lattice(seed, ix, iz), b = Lattice(seed, ix + 1, iz), c = Lattice(seed, ix, iz + 1), e = Lattice(seed, ix + 1, iz + 1);
        return a + (b - a) * tx + (c - a) * tz + (a - b - c + e) * tx * tz;
    }

    private static float Lattice(int seed, int x, int z)
    {
        uint h = unchecked((uint)seed * 0x9E3779B9u ^ (uint)x * 0x85EBCA6Bu ^ (uint)z * 0xC2B2AE35u);
        h ^= h >> 16;
        h = unchecked(h * 0x7FEB352Du);
        h ^= h >> 15;
        h = unchecked(h * 0x846CA68Bu);
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777216f;
    }

    private static float Smooth(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float EdgeDistance(float x, float z) => MathF.Min(MathF.Min(x, z), MathF.Min(Cell - x, Cell - z));

    private static bool Inside(Outline outline, float x, float z)
    {
        bool inside = false;
        for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
        {
            (float X, float Z) a = outline[i], b = outline[j];
            if ((a.Z > z) != (b.Z > z) && x < (b.X - a.X) * (z - a.Z) / (b.Z - a.Z) + a.X) inside = !inside;
        }
        return inside;
    }

    /// <summary>Metres from the point to the closed outline's nearest side.</summary>
    private static float OutlineDistance(Outline outline, float x, float z)
    {
        float least = float.MaxValue;
        for (int i = 0; i < outline.Count; i++)
            least = MathF.Min(least, SegmentDistance(x, z, outline[i], outline[(i + 1) % outline.Count]));
        return least;
    }

    /// <summary>Metres from the point to the open line's nearest segment.</summary>
    private static float PolylineDistance(Outline line, float x, float z)
    {
        float least = float.MaxValue;
        for (int i = 1; i < line.Count; i++)
            least = MathF.Min(least, SegmentDistance(x, z, line[i - 1], line[i]));
        return least;
    }

    private static float SegmentDistance(float x, float z, (float X, float Z) a, (float X, float Z) b)
    {
        float vx = b.X - a.X, vz = b.Z - a.Z;
        float t = Math.Clamp(((x - a.X) * vx + (z - a.Z) * vz) / (vx * vx + vz * vz), 0f, 1f);
        return SceneryDresser.Distance(x, z, a.X + vx * t, a.Z + vz * t);
    }

    /// <summary>
    /// Twice-signed area over two: positive for an outline running counter-clockwise seen from above.
    /// </summary>
    private static float SignedArea(Outline outline)
    {
        float sum = 0f;
        for (int i = 0; i < outline.Count; i++)
        {
            (float X, float Z) a = outline[i], b = outline[(i + 1) % outline.Count];
            sum += a.X * b.Z - b.X * a.Z;
        }
        return sum / 2f;
    }

    private static (float X, float Z) Centroid(Outline outline) => (outline.Average(p => p.X), outline.Average(p => p.Z));

    /// <summary>Refuses an outline that is not convex and counter-clockwise (every turn to the left).</summary>
    private static void Convex(string name, Outline outline)
    {
        for (int i = 0; i < outline.Count; i++)
        {
            (float X, float Z) a = outline[i], b = outline[(i + 1) % outline.Count], c = outline[(i + 2) % outline.Count];
            if ((b.X - a.X) * (c.Z - b.Z) - (b.Z - a.Z) * (c.X - b.X) <= 0f)
                throw new InvalidOperationException($"{name}: the cliff's foot turns right at ({ForestText.N(b.X)}, {ForestText.N(b.Z)}): it must be convex, counter-clockwise");
        }
    }

    /// <summary>
    /// A convex counter-clockwise outline with every side moved <paramref name="by" /> metres in, its corners where the
    /// moved sides meet.
    /// </summary>
    private static Outline Inset(Outline outline, float by)
    {
        int count = outline.Count;
        var lines = new (float X, float Z, float DX, float DZ)[count];
        for (int i = 0; i < count; i++)
        {
            (float X, float Z) a = outline[i], b = outline[(i + 1) % count];
            float length = SceneryDresser.Distance(a.X, a.Z, b.X, b.Z);
            float dx = (b.X - a.X) / length, dz = (b.Z - a.Z) / length;
            lines[i] = (a.X - dz * by, a.Z + dx * by, dx, dz);  // the left of a counter-clockwise side is inside
        }
        var corners = new (float X, float Z)[count];
        for (int i = 0; i < count; i++)
        {
            (float px, float pz, float ux, float uz) = lines[(i + count - 1) % count];
            (float qx, float qz, float vx, float vz) = lines[i];
            float s = ((qx - px) * vz - (qz - pz) * vx) / (ux * vz - uz * vx);
            corners[i] = (px + ux * s, pz + uz * s);
        }
        return corners;
    }
}
