using System.Text;

namespace Avalon.ChunkGen;

/// <summary>
/// Dresses a forest piece with the nature kit's models and the custom avalon_* forest models: the decor file the
/// client draws (Maps/Decor/&lt;chunk&gt;.json), in the look the owner approved (a dense, tall wood around the edges and
/// on the blockers, an open, dark floor between them, few flowers). In order:
/// <list type="number">
/// <item>the paths (<see cref="ForestPaths" />) from every exit to the meeting point, with stepping stones on them;</item>
/// <item>every blocker by its <see cref="Dressing" />: trees standing inside a tree drum near its rim with bushes
/// and a rock filling it; a custom rock and kit rocks fitted into a rock blocker; ruin walls along a wall box with
/// pillar stumps and rubble; a pillar and a stump on a pillar; the custom model of a <see cref="Blocker.Prop" /> once;
/// the tree of a <see cref="Dressing.Trunk" />;</item>
/// <item>undergrowth along the frame's edges outside the exits' throats;</item>
/// <item>ground cover on the open floor off the strip: grass most, ferns, clover, plants, pebbles, a few small bushes
/// and flowers, and mushrooms at the feet of trees and logs.</item>
/// </list>
/// Deterministic: one <see cref="Random" /> seeded from the FNV-1a hash of the piece's name, whole-degree yaws turned
/// through <see cref="Yaw" />, positions written with at most 3 decimals (DecorJsonWriter, which also sorts them), so a
/// rerun on any machine writes the same file. Densities are per 100 m² of the frame; they are tuned against renders.
/// </summary>
public static class ForestDresser
{
    // ---- trees ----

    /// <summary>The scale range of a tree standing in a tree drum.</summary>
    public const float DrumTreeMinScale = 1f, DrumTreeMaxScale = 1.5f;

    /// <summary>How far inside a drum's flat sides a tree's trunk keeps, in metres.</summary>
    public const float DrumTrunkInset = 0.05f;

    /// <summary>At most this many twisted trees (red leaves even darkened) in a frame, and only in an old wood.</summary>
    public const int MaxTwistedTrees = 1;

    // ---- tree walls ----

    /// <summary>The scale range of a tree in a tree wall.</summary>
    public const float WallTreeMinScale = 1f, WallTreeMaxScale = 1.6f;

    /// <summary>How far apart a tree wall's trees stand along one of its rows, drawn in this range; the rows are staggered.</summary>
    public const float WallTreeMinSpacing = 1.5f, WallTreeMaxSpacing = 2.1f;

    /// <summary>A tree wall this deep or deeper carries three rows of trees, a shallower one two.</summary>
    public const float WallThreeRowDepth = 3f;

    /// <summary>How far inside a tree wall's sides a trunk keeps, and how far apart two of its trunks keep, in metres.</summary>
    public const float WallTrunkInset = 0.05f, WallTrunkGap = 0.15f;

    /// <summary>
    /// A dead tree drawn for a tree wall stays one this share of the time (another species is drawn otherwise), at most
    /// <see cref="WallDeadTreeMaxScale" />: a few bare trees among the living ones, not a dead wood.
    /// </summary>
    public const double WallDeadTreeShare = 0.4;

    public const float WallDeadTreeMaxScale = 1.3f;

    /// <summary>Undergrowth among a tree wall's trunks, per 100 m² of the wall.</summary>
    public const float WallFillPer100 = 30f;

    /// <summary>Undergrowth at a tree wall's foot, along its open face: one about every this many metres.</summary>
    public const float WallFootSpacing = 1.2f;

    // ---- ground cover, per 100 m² of the frame ----

    public const float GrassClumpsPer100 = 6f;
    public const float FernsPer100 = 2.5f;
    public const float CloverPer100 = 1.2f;
    public const float PlantsPer100 = 0.6f;
    public const float PebblesPer100 = 1.2f;
    public const float SmallBushesPer100 = 0.5f;
    public const float FlowersPer100 = 0.2f;

    /// <summary>Tall undergrowth in the band along the frame's edges, per 100 m² of the band.</summary>
    public const float EdgeUndergrowthPer100 = 16f;

    /// <summary>The edge band's depth, in metres.</summary>
    public const float EdgeBand = 3.5f;

    // ---- rocks and drums ----

    /// <summary>Kit rocks are added to a rock blocker until they and its custom rock cover this share of it, or no candidate adds much.</summary>
    public const float RockCover = 0.8f;

    /// <summary>At most this many kit rocks on one rock blocker.</summary>
    public const int MaxKitRocks = 5;

    /// <summary>The kit rocks tried for each one placed.</summary>
    public const int RockCandidates = 200;

    /// <summary>The scale range of a kit rock on a rock blocker.</summary>
    public const float MinKitRockScale = 0.5f, MaxKitRockScale = 1.6f;

    /// <summary>How far a kit rock's box may reach past its rock blocker, inside the rules' tolerance.</summary>
    public const float KitRockReach = 0.1f;

    /// <summary>
    /// What a rock blocker's rocks leave bare gets up to this many bushes, ferns or tall plants, each on the barest spot
    /// left and covering what lies within <see cref="BareFillReach" /> of it; on a box of at least
    /// <see cref="RockTreeArea" /> m², the first spot a tree among the rocks, <see cref="RockTreeChance" /> of the time.
    /// </summary>
    public const int MaxBareFill = 6;

    public const float BareFillReach = 1f;

    public const float RockTreeArea = 25f;

    public const double RockTreeChance = 0.5;

    /// <summary>A tree drum's ring of undergrowth stands at this share of its radius, about this many metres apart.</summary>
    public const float DrumFillRing = 0.75f, DrumFillSpacing = 1.4f;

    /// <summary>At most this many red-leaved kit bushes (Bush_Common) in a frame.</summary>
    public const int MaxRedBushes = 1;

    /// <summary>Mushrooms at the foot of each free-standing tree, tree drum and log: from 0 to this many.</summary>
    public const int MaxMushroomsPerFoot = 2;

    /// <summary>A path segment carries a stepping stone this often.</summary>
    public const double StoneChance = 0.45;

    /// <summary>How far ground cover keeps from a strip's centre line (the rules' least).</summary>
    public const float CoverStripClearance = ForestDecorRules.StripClearance;

    /// <summary>How far big cover (ferns, bushes, tall plants) keeps from a strip's centre line.</summary>
    public const float BigCoverStripClearance = ForestPaths.Width / 2f + 1.2f;

    /// <summary>How far ground cover keeps from a doodad slot, and from a doodad_large slot (which may hold a solid doodad).</summary>
    public const float DoodadCoverClearance = 1.5f, LargeDoodadCoverClearance = 3f;

    /// <summary>How far ground cover keeps from a portal slot, so the portal the client draws there stands on bare floor.</summary>
    public const float PortalCoverClearance = 3f;

    private static readonly IReadOnlyDictionary<TreeMix, (TreeSpecies Species, int Weight)[]> s_mixes = new Dictionary<TreeMix, (TreeSpecies, int)[]>
    {
        [TreeMix.Mixed] = [(TreeSpecies.CommonTree, 55), (TreeSpecies.Pine, 35), (TreeSpecies.DeadTree, 10)],
        [TreeMix.Broadleaf] = [(TreeSpecies.CommonTree, 75), (TreeSpecies.Pine, 20), (TreeSpecies.DeadTree, 5)],
        [TreeMix.Pine] = [(TreeSpecies.Pine, 65), (TreeSpecies.CommonTree, 25), (TreeSpecies.DeadTree, 10)],
        [TreeMix.Old] = [(TreeSpecies.CommonTree, 42), (TreeSpecies.Pine, 25), (TreeSpecies.DeadTree, 28), (TreeSpecies.TwistedTree, 5)],
    };

    private static readonly string[] s_rocks = ["Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3"];
    private static readonly string[] s_customRocks = ["avalon_rock_large_1", "avalon_rock_large_2", "avalon_rock_large_3", "avalon_rock_leaning_1", "avalon_rock_leaning_2"];
    private static readonly string[] s_stones = ["RockPath_Round_Small_1", "RockPath_Round_Small_2", "RockPath_Round_Small_3", "RockPath_Round_Thin", "RockPath_Square_Small_1", "RockPath_Square_Small_2", "RockPath_Square_Small_3"];
    private static readonly string[] s_rubble = ["Pebble_Square_1", "Pebble_Square_2", "Pebble_Square_3", "Pebble_Square_4", "Pebble_Square_5", "Pebble_Square_6"];
    private static readonly string[] s_pebbles = ["Pebble_Round_1", "Pebble_Round_2", "Pebble_Round_3", "Pebble_Round_4", "Pebble_Round_5", "Pebble_Square_2", "Pebble_Square_4"];
    private static readonly (string Model, int Weight)[] s_grass = [("Grass_Common_Short", 40), ("Grass_Wispy_Short", 30), ("Grass_Common_Tall", 20), ("Grass_Wispy_Tall", 10)];
    private static readonly (string Model, int Weight)[] s_undergrowth = [("Fern_1", 30), ("Bush_Common_Flowers", 8), ("Plant_1_Big", 12), ("Grass_Wispy_Tall", 35), ("Grass_Common_Tall", 15)];
    private static readonly (string Model, int Weight)[] s_drumFill = [("Bush_Common_Flowers", 35), ("Plant_1_Big", 30), ("Fern_1", 35)];
    private static readonly (string Model, int Weight)[] s_wallFill = [("Fern_1", 40), ("Plant_1_Big", 25), ("Grass_Wispy_Tall", 20), ("Bush_Common_Flowers", 15)];
    private static readonly string[] s_plants = ["Plant_1", "Plant_7", "Plant_7_Big"];
    private static readonly string[] s_clover = ["Clover_1", "Clover_2"];
    private static readonly string[] s_flowers = ["Flower_3_Group", "Flower_4_Group", "Flower_3_Single", "Flower_4_Single"];

    /// <summary>The ruin wall sections, longest first, and their lengths (Kit/forest-footprints.json).</summary>
    private static readonly (string Model, float Length)[] s_walls = [("avalon_ruin_wall_3", 6f), ("avalon_ruin_wall_2", 4.2f), ("avalon_ruin_wall_1", 2.4f)];

    private const string Pillar = "avalon_ruin_pillar_1", Stump = "avalon_ruin_pillar_2";

    /// <summary>A ruin pillar's or stump's half width, and a wall section's half thickness.</summary>
    private const float PillarHalf = 0.65f, WallHalf = 0.4f;

    /// <summary>
    /// A species' trunk radius at scale 1, measured from the kit's bark below 1 m and rounded up to 5 cm: the widest of
    /// its five variants, so any variant fits its blocker.
    /// </summary>
    public static float TrunkRadius(TreeSpecies species) => species switch
    {
        TreeSpecies.CommonTree => 0.65f,
        TreeSpecies.Pine => 0.45f,
        TreeSpecies.DeadTree => 0.75f,
        TreeSpecies.TwistedTree => 1.4f,
        _ => throw new ArgumentOutOfRangeException(nameof(species), species, "unknown species"),
    };

    /// <summary>The radius of a free-standing tree's trunk blocker: its trunk radius at its scale over cos 15°, so the twelve-gon's flat sides reach the trunk, rounded up to 5 cm.</summary>
    public static float TrunkBlockerRadius(TreeSpecies species, float scale) =>
        MathF.Ceiling(TrunkRadius(species) * scale / CylinderBlocker.Apothem * 20f - 0.001f) / 20f;

    /// <summary>A free-standing tree: its trunk blocker, which the tree of that species and scale draws.</summary>
    public static CylinderBlocker Trunk(TreeSpecies species, float scale, float x, float z) =>
        new(x, z, TrunkBlockerRadius(species, scale)) { Dressing = Dressing.Trunk, Tree = new TreePlacement(species, scale) };

    /// <summary>
    /// A custom forest model's blockers at (x, z) and its yaw: each of its footprint parts turned by the yaw, a box as a
    /// box, a circle as a twelve-sided cylinder of its blocker radius, carrying the model and the role its kind gives
    /// it. A box-footprint model turns only by quarter turns (box blockers stay axis-aligned) and a round one only by
    /// multiples of 30 degrees (the twelve-gon never turns); any other yaw, or a model with no solid footprint, is refused.
    /// </summary>
    public static IReadOnlyList<Blocker> Prop(string model, float x, float z, int yaw)
    {
        if (!ForestFootprints.Load().TryGetValue(model, out IReadOnlyList<FootprintPart>? parts) || parts.Count == 0)
            throw new InvalidOperationException($"{model} has no solid footprint in Kit/forest-footprints.json");
        Dressing role = RoleOf(model);
        var placement = new PropPlacement(model, x, z, yaw);
        return [.. PlacedParts(model, parts, x, z, yaw).Select(b => b with { Dressing = role, Prop = placement })];
    }

    /// <summary>The blocker shapes of a custom model's parts at (x, z) and its yaw (see <see cref="Prop" />).</summary>
    internal static IEnumerable<Blocker> PlacedParts(string model, IReadOnlyList<FootprintPart> parts, float x, float z, int yaw)
    {
        int step = parts.Any(p => p is BoxPart) ? 90 : 30;
        if (yaw < 0 || yaw > 359 || yaw % step != 0)
            throw new InvalidOperationException($"{model} has yaw {yaw}: its footprint turns only by multiples of {step} degrees");
        foreach (FootprintPart part in parts)
        {
            switch (part)
            {
                case BoxPart box:
                {
                    (float minX, float maxX, float minZ, float maxZ) = TownBuilding.Footprint(box.MinX, box.MaxX, box.MinZ, box.MaxZ, x, z, yaw / 90);
                    yield return new BoxBlocker(minX, maxX, minZ, maxZ);
                    break;
                }
                case CirclePart circle:
                {
                    (float cx, float cz) = Yaw.Turn(circle.X, circle.Z, yaw);
                    yield return new CylinderBlocker(x + cx, z + cz, circle.BlockerRadius);
                    break;
                }
            }
        }
    }

    private static Dressing RoleOf(string model) => model switch
    {
        _ when model.StartsWith("avalon_log_", StringComparison.Ordinal) => Dressing.Log,
        _ when model.StartsWith("avalon_rock_leaning_", StringComparison.Ordinal) => Dressing.LeaningRock,
        _ when model.StartsWith("avalon_rock_large_", StringComparison.Ordinal) => Dressing.Rock,
        "avalon_rock_arch" => Dressing.ArchLeg,
        "avalon_rock_overhang" => Dressing.Overhang,
        _ when model.StartsWith("avalon_ruin_wall_", StringComparison.Ordinal) => Dressing.RuinWall,
        _ when model.StartsWith("avalon_ruin_pillar_", StringComparison.Ordinal) => Dressing.Pillar,
        _ => throw new InvalidOperationException($"{model} is not a solid custom forest model"),
    };

    /// <summary>A single piece's decor, in its cell; a piece with an undressed blocker is refused (InvalidOperationException).</summary>
    public static IReadOnlyList<DecorItem> Dress(ChunkPiece piece) => Dress(ForestFrame.Of(piece));

    /// <summary>A set piece's decor, in its 60 x 60 m frame (<see cref="Members" /> splits it).</summary>
    public static IReadOnlyList<DecorItem> Dress(ChunkGroupPiece group) => Dress(ForestFrame.Of(group));

    /// <summary>
    /// A set piece's decor split into its members as its slots are: each item to the member whose cell holds its
    /// position (an item on an inner edge to the member east or north of it), moved into that member's own cell.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<DecorItem>> Members(ChunkGroupPiece group, IReadOnlyList<DecorItem> decor)
    {
        const float Cell = ChunkPiece.CellSize;
        var members = new Dictionary<string, IReadOnlyList<DecorItem>>(StringComparer.Ordinal);
        foreach ((ChunkPiece piece, int cellX, int cellZ) in group.Members())
        {
            members[piece.Name] = [.. decor
                .Where(d => CellOf(d.X) == cellX && CellOf(d.Z) == cellZ)
                .Select(d => d with { X = d.X - cellX * Cell, Z = d.Z - cellZ * Cell })];
        }
        return members;

        static int CellOf(float v) => Math.Clamp((int)MathF.Floor(v / Cell), 0, 1);
    }

    internal static IReadOnlyList<DecorItem> Dress(ForestFrame frame)
    {
        Blocker? undressed = frame.Blockers.FirstOrDefault(b => b.Dressing == Dressing.None);
        if (undressed is not null) throw new InvalidOperationException($"{frame.Name}: blocker {ForestText.Of(undressed)} has no dressing");

        var scene = new Scene(frame, new Random(StableSeed(frame.Name)));
        scene.LayPaths();
        foreach (Blocker blocker in frame.Blockers)
            scene.DressBlocker(blocker);
        scene.Undergrowth();
        scene.GroundCover();
        return scene.Items;
    }

    /// <summary>FNV-1a over the name's UTF-8 bytes: a seed no runtime or machine changes (string.GetHashCode does).</summary>
    internal static int StableSeed(string name)
    {
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(name))
        {
            hash ^= b;
            hash *= 16777619;
        }
        return (int)(hash & 0x7FFFFFFF);
    }

    /// <summary>One frame being dressed: the items so far, what ground cover must keep clear of, and the random draws.</summary>
    private sealed class Scene(ForestFrame frame, Random rng)
    {
        private readonly List<(float X, float Z, float Radius)> _cover = [];
        private readonly List<((float X, float Z) A, (float X, float Z) B)> _strip = [];
        private readonly List<(float X, float Z, float Radius)> _feet = [];
        private readonly HashSet<PropPlacement> _props = [];
        private readonly List<(float X, float Z, float Radius)> _wallTrunks = [];
        private int _twisted;
        private int _redBushes;

        public List<DecorItem> Items { get; } = [];

        private IReadOnlyDictionary<string, IReadOnlyList<FootprintPart>> Footprints { get; } = ForestFootprints.Load();

        private IReadOnlyDictionary<string, ModelBounds> Kit { get; } = KitBounds.Load();

        public void LayPaths()
        {
            for (int i = 0; i < frame.Exits.Count; i++)
            {
                foreach (DecorItem segment in ForestPaths.Lay(frame, frame.Exits[i], i, rng))
                {
                    Items.Add(segment);
                    _strip.Add(ForestPaths.Ends(segment));
                    if (rng.NextDouble() < StoneChance)
                    {
                        (float fx, float fz) = Yaw.Forward(segment.Yaw);
                        float along = Uniform(-0.7f, 0.7f), across = Uniform(-0.5f, 0.5f);
                        Add(Pick(s_stones), segment.X + fx * along + fz * across, segment.Z + fz * along - fx * across,
                            rng.Next(360), Round(Uniform(0.8f, 1f)), y: 0.02f);
                    }
                }
            }
            if (frame.Exits.Count > 0) Items.Add(new DecorItem("", ForestPaths.Hub, frame.MeetX, 0f, frame.MeetZ, rng.Next(360)));
        }

        public void DressBlocker(Blocker blocker)
        {
            if (blocker.Prop is { } prop)
            {
                if (!_props.Add(prop)) return;
                Items.Add(new DecorItem("", prop.Model, prop.X, 0f, prop.Z, prop.Yaw));
                if (blocker.Dressing == Dressing.Log) Foot(prop.X, prop.Z, 2.5f);
                return;
            }

            switch (blocker.Dressing, blocker)
            {
                case (Dressing.Trunk, CylinderBlocker trunk):
                    TreePlacement tree = trunk.Tree ?? throw new InvalidOperationException($"{frame.Name}: trunk {ForestText.Of(trunk)} names no tree");
                    AddTree(tree.Species, tree.Scale, trunk.X, trunk.Z);
                    Foot(trunk.X, trunk.Z, trunk.Radius);
                    break;
                case (Dressing.TreeDrum, CylinderBlocker drum):
                    DressDrum(drum);
                    break;
                case (Dressing.Rock, _):
                    DressRocks(blocker);
                    break;
                case (Dressing.RuinWall, BoxBlocker wall):
                    DressWall(wall);
                    break;
                case (Dressing.Pillar, CylinderBlocker pillar):
                    DressPillar(pillar);
                    break;
                case (Dressing.TreeWall, BoxBlocker wall):
                    DressTreeWall(wall);
                    break;
                default:
                    throw new InvalidOperationException($"{frame.Name}: blocker {ForestText.Of(blocker)} is a shape the dresser cannot draw {blocker.Dressing} on without a prop");
            }
        }

        /// <summary>Two to four trees inside the drum near its rim, trunks within its flat sides; bushes, a fern or a tall plant and, in a wide one, a rock filling it.</summary>
        private void DressDrum(CylinderBlocker drum)
        {
            int count = drum.Radius >= 2.5f ? 4 : drum.Radius >= 2f ? rng.Next(3, 5) : rng.Next(2, 4);
            int start = rng.Next(360);
            for (int i = 0; i < count; i++)
            {
                TreeSpecies species = PickSpecies();
                float scale = Round(Uniform(DrumTreeMinScale, DrumTreeMaxScale), 0.05f);
                float reach = drum.Radius * CylinderBlocker.Apothem - DrumTrunkInset - ForestDresser.TrunkRadius(species) * scale;
                if (reach < 0f)
                {
                    species = TreeSpecies.Pine;
                    scale = DrumTreeMinScale;
                    reach = Math.Max(0f, drum.Radius * CylinderBlocker.Apothem - DrumTrunkInset - ForestDresser.TrunkRadius(species) * scale);
                }
                (float sin, float cos) = Yaw.Of(start + i * 360 / count + rng.Next(-15, 16));
                // Rounded down to the centimetre, so the trunk stays within the drum.
                float rho = MathF.Floor(reach * Uniform(0.85f, 1f) * 100f) / 100f;
                AddTree(species, scale, drum.X + rho * cos, drum.Z + rho * sin, round: false);
            }

            // A ring of undergrowth just inside the rim, so the drum's wall reads as a thicket all round, and a rock or a bush at its heart.
            int ring = (int)MathF.Ceiling(2f * MathF.PI * DrumFillRing * drum.Radius / DrumFillSpacing);
            int turn = rng.Next(360);
            for (int i = 0; i < ring; i++)
            {
                (float sin, float cos) = Yaw.Of(turn + i * 360 / ring + rng.Next(-10, 11));
                float rho = drum.Radius * Uniform(DrumFillRing - 0.15f, DrumFillRing + 0.15f);
                Add(PickWeighted(s_drumFill), drum.X + rho * cos, drum.Z + rho * sin, rng.Next(360), Round(Uniform(0.8f, 1.2f)));
            }
            if (drum.Radius < 2f || rng.NextDouble() >= 0.6 || !FitKitRock(drum, drum.X, drum.Z, 0.35f, 0.5f))
                Add(PickWeighted(s_drumFill), drum.X, drum.Z, rng.Next(360), Round(Uniform(0.9f, 1.2f)));
            Foot(drum.X, drum.Z, drum.Radius);
        }

        /// <summary>The custom rock that fills the blocker best, then up to a few kit rocks fitted into what is left, and pebbles at its foot.</summary>
        private void DressRocks(Blocker blocker)
        {
            (float minX, float maxX, float minZ, float maxZ) = blocker.Bounds;
            float cx = (minX + maxX) / 2f, cz = (minZ + maxZ) / 2f;
            List<(float Area, string Model, int Yaw, float X, float Z)> fits = [];
            foreach (string model in s_customRocks)
            {
                IReadOnlyList<FootprintPart> parts = Footprints[model];
                int step = parts.Any(p => p is BoxPart) ? 90 : 30;
                for (int yaw = 0; yaw < 360; yaw += step)
                {
                    // Centre the turned parts' bounds on the blocker's centre.
                    List<Blocker> placed = [.. PlacedParts(model, parts, 0f, 0f, yaw)];
                    float px = cx - (placed.Min(b => b.Bounds.MinX) + placed.Max(b => b.Bounds.MaxX)) / 2f;
                    float pz = cz - (placed.Min(b => b.Bounds.MinZ) + placed.Max(b => b.Bounds.MaxZ)) / 2f;
                    px = Round(px);
                    pz = Round(pz);
                    if (ForestDecorRules.PartsWithin(model, parts, px, pz, yaw, blocker, 0f))
                        fits.Add((parts.Sum(Area), model, yaw, px, pz));
                }
            }

            // How much of the blocker the rocks cover, on a lattice: a custom rock's parts, a kit rock's ellipse inside its box.
            List<(float X, float Z)> lattice = Lattice(blocker);
            bool[] covered = new bool[lattice.Count];
            int rocks = 0;
            if (fits.Count > 0)
            {
                float best = fits.Max(f => f.Area);
                List<(float Area, string Model, int Yaw, float X, float Z)> near = [.. fits.Where(f => f.Area >= best * 0.85f)];
                (_, string model, int yaw, float x, float z) = near[rng.Next(near.Count)];
                Items.Add(new DecorItem("", model, x, 0f, z, yaw));
                for (int i = 0; i < lattice.Count; i++)
                    covered[i] = ForestDecorRules.PartsCover(Footprints[model], x, z, yaw, lattice[i].X, lattice[i].Z);
                rocks++;
            }

            // Kit rocks, each the candidate that covers the most still bare, until the rocks cover enough of it.
            for (int k = 0; k < MaxKitRocks && covered.Count(c => c) < lattice.Count * RockCover; k++)
            {
                (int Gain, string Model, int Yaw, float Scale, float X, float Z) best = default;
                for (int c = 0; c < RockCandidates; c++)
                {
                    string model = Pick(s_rocks);
                    int yaw = rng.Next(360);
                    float scale = Round(Uniform(MinKitRockScale, MaxKitRockScale), 0.05f);
                    float x = Round(Uniform(minX, maxX)), z = Round(Uniform(minZ, maxZ));
                    if (!ForestDecorRules.KitRockWithin(Kit[model], x, z, yaw, scale, blocker, KitRockReach)) continue;
                    int gain = 0;
                    for (int i = 0; i < lattice.Count; i++)
                    {
                        if (!covered[i] && InRock(Kit[model], x, z, yaw, scale, lattice[i])) gain++;
                    }
                    if (gain > best.Gain) best = (gain, model, yaw, scale, x, z);
                }
                if (best.Model is null || best.Gain < Math.Max(2, lattice.Count / 25)) break;
                Items.Add(new DecorItem("", best.Model, best.X, 0f, best.Z, best.Yaw, best.Scale));
                for (int i = 0; i < lattice.Count; i++)
                    covered[i] |= InRock(Kit[best.Model], best.X, best.Z, best.Yaw, best.Scale, lattice[i]);
                rocks++;
            }
            if (rocks == 0) throw new InvalidOperationException($"{frame.Name}: no rock fits blocker {ForestText.Of(blocker)}");

            // What the rocks leave bare (a box's corners, mostly): a tree among the rocks in a big one, undergrowth on the rest.
            bool tree = (maxX - minX) * (maxZ - minZ) >= RockTreeArea && rng.NextDouble() < RockTreeChance;
            for (int fill = 0; fill < MaxBareFill; fill++)
            {
                int spot = -1, most = 0;
                for (int i = 0; i < lattice.Count; i++)
                {
                    if (covered[i]) continue;
                    int around = 0;
                    for (int j = 0; j < lattice.Count; j++)
                    {
                        if (!covered[j] && ForestPaths.Distance(lattice[i].X, lattice[i].Z, lattice[j].X, lattice[j].Z) <= BareFillReach) around++;
                    }
                    if (around > most) (spot, most) = (i, around);
                }
                if (spot < 0 || most < 3) break;
                (float x, float z) = lattice[spot];
                if (tree)
                {
                    tree = false;
                    TreeSpecies species = PickSpecies();
                    float scale = Round(Uniform(DrumTreeMinScale, DrumTreeMaxScale), 0.05f);
                    CylinderBlocker trunk = new(x, z, ForestDresser.TrunkRadius(species) * scale / CylinderBlocker.Apothem);
                    if (blocker is BoxBlocker box && trunk.Bounds.MinX >= box.MinX && trunk.Bounds.MaxX <= box.MaxX
                        && trunk.Bounds.MinZ >= box.MinZ && trunk.Bounds.MaxZ <= box.MaxZ)
                    {
                        AddTree(species, scale, x, z);
                        Foot(x, z, trunk.Radius);
                    }
                }
                Add(PickWeighted(s_drumFill), x, z, rng.Next(360), Round(Uniform(0.8f, 1.1f)));
                for (int j = 0; j < lattice.Count; j++)
                    covered[j] |= ForestPaths.Distance(x, z, lattice[j].X, lattice[j].Z) <= BareFillReach;
            }

            float width = maxX - minX, depth = maxZ - minZ;

            for (int i = rng.Next(2, 5); i > 0; i--)
            {
                (float sin, float cos) = Yaw.Of(rng.Next(360));
                float reach = MathF.Max(width, depth) / 2f + Uniform(0.1f, 0.6f);
                TryCover(Pick(s_pebbles), cx + reach * cos, cz + reach * sin, Round(Uniform(1f, 2f)), 0.3f);
            }
        }

        /// <summary>Points 0.5 m apart over the blocker's footprint (a cylinder's inside its flat sides).</summary>
        private static List<(float X, float Z)> Lattice(Blocker blocker)
        {
            (float minX, float maxX, float minZ, float maxZ) = blocker.Bounds;
            List<(float X, float Z)> points = [];
            for (float x = minX + 0.25f; x < maxX; x += 0.5f)
            {
                for (float z = minZ + 0.25f; z < maxZ; z += 0.5f)
                {
                    if (blocker is not CylinderBlocker c || ForestPaths.Distance(x, z, c.X, c.Z) <= c.Radius * CylinderBlocker.Apothem)
                        points.Add((x, z));
                }
            }
            return points;
        }

        /// <summary>Whether a point lies in a kit rock: the ellipse inside its scaled box, turned by its yaw, a rock's rounded outline.</summary>
        private static bool InRock(ModelBounds b, float x, float z, int yaw, float scale, (float X, float Z) point)
        {
            (float u, float v) = Yaw.Turn(point.X - x, point.Z - z, -yaw);
            float a = (b.MaxX - b.MinX) / 2f * scale, c = (b.MaxZ - b.MinZ) / 2f * scale;
            float du = (u - (b.MinX + b.MaxX) / 2f * scale) / a, dv = (v - (b.MinZ + b.MaxZ) / 2f * scale) / c;
            return du * du + dv * dv <= 1f;
        }

        /// <summary>A kit rock at (x, z) at the largest scale in [min, max] (drawn at random) that fits the blocker at a random yaw, if any does.</summary>
        private bool FitKitRock(Blocker blocker, float x, float z, float minScale, float maxScale)
        {
            string model = Pick(s_rocks);
            int yaw = rng.Next(360);
            x = Round(x);
            z = Round(z);
            for (float scale = Round(Uniform(minScale, maxScale), 0.05f); scale >= minScale - 0.001f; scale -= 0.05f)
            {
                float s = Round(scale, 0.05f);
                if (ForestDecorRules.KitRockWithin(Kit[model], x, z, yaw, s, blocker, 0f))
                {
                    Items.Add(new DecorItem("", model, x, 0f, z, yaw, s));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Wall sections end to end along the box's long axis, as long as fits, centred on a line inside its thickness;
        /// pillars at the ends it leaves, stumps along the other side when the box is thick enough; rubble over it.
        /// </summary>
        private void DressWall(BoxBlocker wall)
        {
            bool alongX = wall.MaxX - wall.MinX >= wall.MaxZ - wall.MinZ;
            (float from, float to, float near, float far) = alongX ? (wall.MinX, wall.MaxX, wall.MinZ, wall.MaxZ) : (wall.MinZ, wall.MaxZ, wall.MinX, wall.MaxX);
            float length = to - from, thickness = far - near;
            List<(string Model, float Length)> sections = Sections(length);
            float used = sections.Sum(s => s.Length);
            bool stumps = thickness >= 2 * WallHalf + 2 * PillarHalf + 0.1f;
            // A thick box keeps its far side for stumps; otherwise the wall stands anywhere across it.
            float line = Round(stumps ? near + WallHalf + 0.05f : near + WallHalf + Uniform(0f, Math.Max(0f, thickness - 2 * WallHalf)));

            float offset = Round(Uniform(0f, length - used));
            float at = from + offset;
            foreach ((string model, float sectionLength) in sections.OrderBy(_ => rng.Next()))
            {
                float centre = Round(at + sectionLength / 2f);
                int yaw = (alongX ? 0 : 90) + (rng.Next(2) == 0 ? 0 : 180);
                (float x, float z) = alongX ? (centre, line) : (line, centre);
                Items.Add(new DecorItem("", model, x, 0f, z, yaw));
                at += sectionLength;
            }

            // A pillar at each end the sections leave room at, and stumps along the far side of a thick box.
            if (offset >= 2 * PillarHalf && rng.NextDouble() < 0.6) AddPillar(alongX, from + PillarHalf, line, wall);
            if (length - used - offset >= 2 * PillarHalf && rng.NextDouble() < 0.6) AddPillar(alongX, to - PillarHalf, line, wall);
            if (stumps)
            {
                float stumpLine = far - PillarHalf;
                for (float along = from + PillarHalf + Uniform(0f, 1.5f); along <= to - PillarHalf; along += Uniform(2.5f, 4f))
                    AddPillar(alongX, Round(along), stumpLine, wall, Stump);
            }

            for (int i = (int)MathF.Round(length / 1.5f); i > 0; i--)
            {
                float a = Uniform(from - 0.5f, to + 0.5f), b = Uniform(near - 0.8f, far + 0.8f);
                (float x, float z) = alongX ? (a, b) : (b, a);
                TryCover(Pick(s_rubble), x, z, Round(Uniform(2f, 3.5f)), 0.4f, onBlockers: true);
            }
        }

        private void AddPillar(bool alongX, float along, float across, Blocker within, string? model = null)
        {
            model ??= rng.Next(2) == 0 ? Pillar : Stump;
            (float x, float z) = alongX ? (Round(along), Round(across)) : (Round(across), Round(along));
            int yaw = rng.Next(4) * 90;
            if (ForestDecorRules.PartsWithin(model, Footprints[model], x, z, yaw, within, 0f)) Items.Add(new DecorItem("", model, x, 0f, z, yaw));
        }

        /// <summary>The wall sections, longest first, whose lengths together come nearest <paramref name="length" /> without passing it.</summary>
        private static List<(string Model, float Length)> Sections(float length)
        {
            List<(string, float)> best = [];
            float bestLength = 0f;
            foreach ((string, float) a in s_walls.Prepend((string.Empty, 0f)))
            {
                foreach ((string, float) b in s_walls.Prepend((string.Empty, 0f)))
                {
                    foreach ((string, float) c in s_walls.Prepend((string.Empty, 0f)))
                    {
                        float total = a.Item2 + b.Item2 + c.Item2;
                        if (total <= length + 0.001f && total > bestLength + 0.001f)
                        {
                            bestLength = total;
                            best = [.. new[] { a, b, c }.Where(s => s.Item1.Length > 0)];
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>A ruin pillar and a stump side by side across the drum (the pillar alone in a narrow one), rubble and a fern at its foot.</summary>
        private void DressPillar(CylinderBlocker drum)
        {
            int yaw = rng.Next(4) * 90;
            bool pair = drum.Radius * CylinderBlocker.Apothem >= MathF.Sqrt((0.6f + PillarHalf) * (0.6f + PillarHalf) + PillarHalf * PillarHalf);
            (float ox, float oz) = Yaw.Turn(pair ? 0.6f : 0f, 0f, yaw);
            Items.Add(new DecorItem("", Pillar, Round(drum.X - ox), 0f, Round(drum.Z - oz), yaw));
            if (pair) Items.Add(new DecorItem("", Stump, Round(drum.X + ox), 0f, Round(drum.Z + oz), Yaw.Normal(yaw + 90 * rng.Next(4))));
            for (int i = rng.Next(2, 4); i > 0; i--)
            {
                (float sin, float cos) = Yaw.Of(rng.Next(360));
                float rho = drum.Radius + Uniform(-0.3f, 0.6f);
                TryCover(Pick(s_rubble), drum.X + rho * cos, drum.Z + rho * sin, Round(Uniform(1.5f, 3f)), 0.4f, onBlockers: true);
            }
        }

        /// <summary>
        /// Two or three rows of trees along the wall, each row's trees about <see cref="WallTreeMinSpacing" /> to
        /// <see cref="WallTreeMaxSpacing" /> apart and staggered against the next row's, so the canopies overlap; every
        /// trunk within the box. Then undergrowth among the trunks, at full size, and a skirt of it along the wall's open
        /// face (the side away from the frame's edge), kept small off the box.
        /// </summary>
        private void DressTreeWall(BoxBlocker wall)
        {
            bool alongX = wall.MaxX - wall.MinX >= wall.MaxZ - wall.MinZ;
            (float from, float to, float near, float far) = alongX ? (wall.MinX, wall.MaxX, wall.MinZ, wall.MaxZ) : (wall.MinZ, wall.MaxZ, wall.MinX, wall.MaxX);
            float length = to - from, depth = far - near;
            int rows = depth >= WallThreeRowDepth ? 3 : 2;
            // The open face: the long side farther from the frame's edge; its row is the front.
            bool openFar = alongX ? wall.MinZ < frame.Size - wall.MaxZ : wall.MinX < frame.Size - wall.MaxX;
            int planted = 0;
            for (int row = 0; row < rows; row++)
            {
                float across = near + depth * (row + 0.5f) / rows;
                float first = from + (WallTreeMinSpacing + WallTreeMaxSpacing) / 2f * row / rows + Uniform(0f, 0.6f);
                bool front = row == (openFar ? rows - 1 : 0);
                for (float along = first; along < to; along += Uniform(WallTreeMinSpacing, WallTreeMaxSpacing))
                {
                    if (TryWallTree(alongX, from, to, near, far, along, across + Uniform(-0.3f, 0.3f), front)) planted++;
                }
            }
            // A short wall the rows leave short of its share gets trees anywhere in it.
            int needed = (int)MathF.Ceiling(length / ForestDecorRules.TreeWallSpacing - 0.001f);
            for (int attempt = 0; planted < needed && attempt < 400; attempt++)
            {
                if (TryWallTree(alongX, from, to, near, far, Uniform(from, to), Uniform(near, far), false)) planted++;
            }
            if (planted < needed)
                throw new InvalidOperationException($"{frame.Name}: tree wall {ForestText.Of(wall)} holds only {planted} trees, not {needed}");

            float area = length * depth;
            for (int i = (int)MathF.Round(area / 100f * WallFillPer100), attempts = 0; i > 0 && attempts < 400; attempts++)
            {
                string model = PickWeighted(s_wallFill);
                (float a, float b) = (Uniform(from + 0.2f, to - 0.2f), Uniform(near + 0.2f, far - 0.2f));
                (float x, float z) = alongX ? (a, b) : (b, a);
                if (TryCover(model, x, z, Round(Uniform(0.8f, 1.2f)), Radius(model) * 0.7f, big: true, onBlockers: true)) i--;
            }

            float face = openFar ? far : near, outward = openFar ? 1f : -1f;
            for (float along = from + Uniform(0f, WallFootSpacing); along < to; along += WallFootSpacing * Uniform(0.7f, 1.3f))
            {
                string model = PickWeighted(s_undergrowth);
                float off = face + outward * Uniform(0.3f, 1.1f);
                (float x, float z) = alongX ? (along, off) : (off, along);
                TryCover(model, x, z, Round(Uniform(0.9f, 1.4f)), Radius(model), big: true);
            }
        }

        /// <summary>
        /// A tree of the piece's mix (never a twisted one, a dead one less often) at a scale in the wall range near (along, across), moved inside the wall's box so its
        /// trunk stays within it (a pine at the least scale when nothing wider fits), unless it would stand on another of
        /// the frame's wall trunks; one in the front row gets mushrooms at its foot.
        /// </summary>
        private bool TryWallTree(bool alongX, float from, float to, float near, float far, float along, float across, bool front)
        {
            TreeSpecies species = PickSpecies();
            if (species == TreeSpecies.DeadTree && rng.NextDouble() >= WallDeadTreeShare) species = PickSpecies();
            // A twisted tree's red crown stays a rare accent of the drums: a wall of them would read red.
            if (species == TreeSpecies.TwistedTree) species = TreeSpecies.CommonTree;
            float scale = Round(Uniform(WallTreeMinScale, species == TreeSpecies.DeadTree ? WallDeadTreeMaxScale : WallTreeMaxScale), 0.05f);
            float radius = ForestDresser.TrunkRadius(species) * scale;
            if (2f * (radius + WallTrunkInset) > far - near)
            {
                species = TreeSpecies.Pine;
                scale = WallTreeMinScale;
                radius = ForestDresser.TrunkRadius(species) * scale;
            }
            float keep = radius + WallTrunkInset + 0.01f;   // a centimetre more, for the rounding to centimetres
            if (2f * keep > far - near || 2f * keep > to - from) return false;
            along = Round(Math.Clamp(along, from + keep, to - keep));
            across = Round(Math.Clamp(across, near + keep, far - keep));
            (float x, float z) = alongX ? (along, across) : (across, along);
            if (_wallTrunks.Any(t => ForestPaths.Distance(x, z, t.X, t.Z) < radius + t.Radius + WallTrunkGap)) return false;
            Items.Add(new DecorItem("", $"{species}_{rng.Next(1, 6)}", x, 0f, z, rng.Next(360), scale));
            _wallTrunks.Add((x, z, radius));
            if (front && rng.NextDouble() < 0.4) Foot(x, z, radius);
            return true;
        }

        /// <summary>Tall undergrowth (ferns, bushes, tall plants and grass) in the band along every edge of the frame, outside the exits' throats.</summary>
        public void Undergrowth()
        {
            float size = frame.Size;
            float area = 4f * size * EdgeBand;
            int count = (int)MathF.Round(area / 100f * EdgeUndergrowthPer100);
            for (int i = 0, attempts = 0; i < count && attempts < count * 30; attempts++)
            {
                float along = Uniform(0.3f, size - 0.3f), depth = Uniform(0.3f, EdgeBand);
                (float x, float z) = rng.Next(4) switch
                {
                    0 => (along, depth),
                    1 => (along, size - depth),
                    2 => (depth, along),
                    _ => (size - depth, along),
                };
                string model = PickWeighted(s_undergrowth);
                if (TryCover(model, x, z, Round(Uniform(0.9f, 1.4f)), Radius(model), big: true)) i++;
            }
        }

        /// <summary>Grass in clumps, ferns, clover, plants, pebbles, a few small bushes and flowers on the open floor; mushrooms at the feet of trees and logs.</summary>
        public void GroundCover()
        {
            float area = frame.Size * frame.Size / 100f;
            for (int clumps = (int)MathF.Round(area * GrassClumpsPer100), attempts = 0; clumps > 0 && attempts < 4000; attempts++)
            {
                (float x, float z) = RandomPoint();
                if (!Open(x, z, 0.4f, big: false)) continue;
                clumps--;
                for (int tufts = rng.Next(2, 5); tufts > 0; tufts--)
                    TryCover(PickWeighted(s_grass), x + Uniform(-0.9f, 0.9f), z + Uniform(-0.9f, 0.9f), Round(Uniform(0.7f, 1.1f)), 0.35f);
            }

            Scatter(() => "Fern_1", area * FernsPer100, 0.7f, 1.1f, big: true);
            Scatter(() => Pick(s_clover), area * CloverPer100, 0.7f, 1f, big: false);
            Scatter(() => Pick(s_plants), area * PlantsPer100, 0.8f, 1.2f, big: false);
            Scatter(() => Pick(s_pebbles), area * PebblesPer100, 1f, 2.5f, big: false);
            Scatter(SmallBush, area * SmallBushesPer100, 0.6f, 0.9f, big: true);
            Scatter(() => Pick(s_flowers), area * FlowersPer100, 0.7f, 1f, big: false);

            foreach ((float fx, float fz, float radius) in _feet)
            {
                for (int i = rng.Next(0, MaxMushroomsPerFoot + 1); i > 0; i--)
                {
                    (float sin, float cos) = Yaw.Of(rng.Next(360));
                    float rho = radius + Uniform(0.3f, 1.2f);
                    string model = rng.NextDouble() < 0.8 ? "Mushroom_Common" : "Mushroom_Laetiporus";
                    TryCover(model, fx + rho * cos, fz + rho * sin, Round(Uniform(0.7f, 1.1f)), 0.25f);
                }
            }
        }

        private string SmallBush()
        {
            if (_redBushes < MaxRedBushes && rng.NextDouble() < 0.25)
            {
                _redBushes++;
                return "Bush_Common";
            }
            return "Bush_Common_Flowers";
        }

        private void Scatter(Func<string> model, float count, float minScale, float maxScale, bool big)
        {
            int target = (int)MathF.Round(count);
            for (int placed = 0, attempts = 0; placed < target && attempts < target * 40; attempts++)
            {
                (float x, float z) = RandomPoint();
                string name = model();
                if (TryCover(name, x, z, Round(Uniform(minScale, maxScale)), Radius(name), big)) placed++;
            }
        }

        private (float X, float Z) RandomPoint() => (Uniform(0.3f, frame.Size - 0.3f), Uniform(0.3f, frame.Size - 0.3f));

        /// <summary>The spacing radius ground cover keeps around a model.</summary>
        private static float Radius(string model) => model switch
        {
            "Fern_1" => 1f,
            "Plant_1_Big" or "Bush_Common_Flowers" or "Bush_Common" => 0.9f,
            _ when model.StartsWith("Grass_", StringComparison.Ordinal) => 0.4f,
            _ when model.StartsWith("Pebble_", StringComparison.Ordinal) => 0.25f,
            _ => 0.45f,
        };

        /// <summary>
        /// Places a walk-through item when its spot is open: inside the frame, off the strip and the hub, outside every
        /// blocker (unless <paramref name="onBlockers" />), clear of the doodad and portal slots and of other cover, and
        /// inside an exit's throat only when it is grass or low; off a blocker, its scale is cut so it is drawn no taller
        /// than ForestDecorRules.WalkThroughCap.
        /// </summary>
        private bool TryCover(string model, float x, float z, float scale, float radius, bool big = false, bool onBlockers = false)
        {
            x = Round(x);
            z = Round(z);
            if (!Open(x, z, radius, big, onBlockers)) return false;
            // Off a blocker, cover reads as small: no taller than the rules' cap, drawn.
            if (!frame.Blockers.Any(b => b.DistanceTo(x, z) == 0f))
                scale = Math.Min(scale, MathF.Floor(ForestDecorRules.WalkThroughCap / Kit[model].MaxY * 100f) / 100f);
            if (frame.Exits.Any(e => e.InThroat(x, z)) && !ForestDecorRules.MayStandInThroat(model, scale, Kit)) return false;
            Items.Add(new DecorItem("", model, x, 0f, z, rng.Next(360), scale));
            _cover.Add((x, z, radius));
            return true;
        }

        private bool Open(float x, float z, float radius, bool big, bool onBlockers = false)
        {
            if (x < 0.2f || z < 0.2f || x > frame.Size - 0.2f || z > frame.Size - 0.2f) return false;
            float strip = big ? BigCoverStripClearance : CoverStripClearance;
            if (_strip.Any(s => ForestPaths.DistanceToSegment(x, z, s.A, s.B) < strip)) return false;
            if (frame.Exits.Count > 0 && ForestPaths.Distance(x, z, frame.MeetX, frame.MeetZ) < ForestPaths.HubRadius + (big ? 1.5f : 0.5f)) return false;
            if (!onBlockers && frame.Blockers.Any(b => b.DistanceTo(x, z) < 0.2f)) return false;
            foreach (DoodadSlot slot in frame.DoodadSlots)
            {
                float keep = slot.Tag == "doodad_large" ? LargeDoodadCoverClearance : DoodadCoverClearance;
                if (ForestPaths.Distance(x, z, slot.X, slot.Z) < keep) return false;
            }
            if (frame.PortalSlots.Any(p => ForestPaths.Distance(x, z, p.X, p.Z) < PortalCoverClearance)) return false;
            return _cover.All(c => ForestPaths.Distance(x, z, c.X, c.Z) >= (c.Radius + radius) * 0.6f);
        }

        private void AddTree(TreeSpecies species, float scale, float x, float z, bool round = true)
        {
            if (species == TreeSpecies.TwistedTree) _twisted++;
            string model = $"{species}_{rng.Next(1, 6)}";
            Items.Add(new DecorItem("", model, round ? Round(x) : x, 0f, round ? Round(z) : z, rng.Next(360), scale));
        }

        private void Foot(float x, float z, float radius) => _feet.Add((x, z, radius));

        private TreeSpecies PickSpecies()
        {
            (TreeSpecies Species, int Weight)[] mix = s_mixes[frame.Hints.Trees];
            while (true)
            {
                int roll = rng.Next(mix.Sum(m => m.Weight));
                foreach ((TreeSpecies species, int weight) in mix)
                {
                    if (roll < weight)
                    {
                        if (species != TreeSpecies.TwistedTree || _twisted < MaxTwistedTrees) return species;
                        break;
                    }
                    roll -= weight;
                }
            }
        }

        private void Add(string model, float x, float z, int yaw, float scale, float y = 0f) =>
            Items.Add(new DecorItem("", model, Round(x), y, Round(z), yaw, scale));

        private string Pick(string[] models) => models[rng.Next(models.Length)];

        private string PickWeighted((string Model, int Weight)[] models)
        {
            int roll = rng.Next(models.Sum(m => m.Weight));
            foreach ((string model, int weight) in models)
            {
                if (roll < weight) return model;
                roll -= weight;
            }
            return models[^1].Model;
        }

        private float Uniform(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private static float Area(FootprintPart part) => part switch
        {
            BoxPart b => (b.MaxX - b.MinX) * (b.MaxZ - b.MinZ),
            CirclePart c => MathF.PI * c.Radius * c.Radius,
            _ => 0f,
        };

        private static float Round(float value, float step = 0.01f) => MathF.Round(value / step) * step;
    }
}
