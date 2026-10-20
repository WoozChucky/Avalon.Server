namespace Avalon.ChunkGen;

/// <summary>
/// Dresses one scenery piece in piece-local metres (0-30 on x and z): the items so far, the spots they take, the
/// ancient crowns overhead and the random draws. Scenery has no blockers, so it shares the forest dresser's tree mixes,
/// models, seed (<see cref="ForestDresser.StableSeed" /> of the piece's name) and yaw table, not its blocker-driven
/// scene. Every item stands on the terrain (its height at the item's position, less a sink) and inside its cell: an
/// item whose turned and scaled kit box would cross the cell's edge is refused silently, so a caller tries another spot
/// or scale. Positions are rounded to the centimetre and scales to 0.01, as the forest's decor is, so a rerun on any
/// machine writes the same file.
/// </summary>
internal sealed class SceneryDresser(string name, SceneryTerrain terrain, IReadOnlyDictionary<string, ModelBounds> kit)
{
    /// <summary>The barrier band's undergrowth stands this far in from an edge, in metres.</summary>
    public const float UndergrowthFrom = 0.6f, UndergrowthTo = 4f;

    /// <summary>The barrier band's two rows of tree trunks stand this far in from an edge, in metres.</summary>
    public const float TrunkFrom = 3.9f, TrunkTo = 5.8f;

    /// <summary>
    /// How far apart the trunks of one row of the band stand, drawn in this range; the rows are staggered.
    /// </summary>
    public const float BandTreeMinSpacing = 6.5f, BandTreeMaxSpacing = 9f;

    /// <summary>The band's undergrowth, per 100 m² of the strip it stands in.</summary>
    public const float BandUndergrowthPer100 = 24f;

    /// <summary>
    /// The band's trees are drawn at this scale range, below the tree walls' largest (the rules' cap): kept lower than
    /// a chunk's walls so the feature behind them shows.
    /// </summary>
    public const float BandTreeMinScale = 0.85f, BandTreeMaxScale = 1.25f;

    /// <summary>A kit tree under an ancient crown keeps its top this many metres below the crown's base.</summary>
    public const float CrownHeadroom = 1f;

    /// <summary>The smallest scale a kit tree is drawn at, here or under a crown.</summary>
    public const float MinTreeScale = 0.8f;

    private const float Cell = ChunkPiece.CellSize;

    /// <summary>The ancient trees' crown bases at scale 1 (forest-kit NOTES: each model's crown base).</summary>
    private static readonly float[] s_ancientCrownBase = [9.56f, 11.44f, 9.43f, 11.7f];

    /// <summary>The ancient trees' trunk radius on the ground, flare and buttresses included, at scale 1.</summary>
    private static readonly float[] s_ancientTrunk = [1.9f, 2.2f, 1.7f, 2f];

    /// <summary>The ancient trees' crown half-width at scale 1 (every part within x, z ±5.9).</summary>
    private const float AncientCrown = 5.9f;

    private static readonly (string Model, int Weight)[] s_bandUndergrowth =
    [
        ("Fern_1", 30), ("Plant_1_Big", 12), ("avalon_hedge_dark", 12), ("avalon_bramble_1", 6), ("avalon_bramble_2", 4),
        ("Bush_Common_Flowers", 3), ("Grass_Wispy_Tall", 16), ("Grass_Common_Tall", 6),
    ];

    /// <summary>The darker understorey of the old woods: ferns, big plants, dark hedges and brambles.</summary>
    private static readonly (string Model, int Weight)[] s_understorey =
    [
        ("Fern_1", 34), ("Plant_1_Big", 14), ("avalon_hedge_dark", 10), ("avalon_bramble_1", 4), ("avalon_bramble_2", 3),
        ("Grass_Wispy_Tall", 14), ("Plant_1", 8),
    ];

    private static readonly (string Model, int Weight)[] s_groundCover =
    [
        ("Grass_Common_Short", 20), ("Grass_Wispy_Short", 18), ("Clover_1", 6), ("Clover_2", 6), ("Plant_7", 5), ("Plant_7_Big", 4),
        ("Plant_1", 6), ("Fern_1", 8), ("Pebble_Round_2", 3), ("Pebble_Round_4", 3), ("Pebble_Square_4", 2), ("Mushroom_Common", 3),
        ("Mushroom_Laetiporus", 1),
    ];

    private static readonly string[] s_gapFill = ["Fern_1", "avalon_hedge_dark", "avalon_bramble_1"];

    private readonly SceneryTerrain _terrain = terrain;
    private readonly IReadOnlyDictionary<string, ModelBounds> _bounds = kit;
    private readonly List<(float X, float Z, float Radius)> _taken = [];
    private readonly List<(float X, float Z, float Radius, float Base)> _crowns = [];

    /// <summary>The piece's name, in refusals.</summary>
    public string Name { get; } = name;

    /// <summary>The piece's random draws, seeded from its name.</summary>
    public Random Rng { get; } = new(ForestDresser.StableSeed(name));

    /// <summary>The items so far, in piece-local metres.</summary>
    public List<DecorItem> Items { get; } = [];

    /// <summary>
    /// The decor file's kit: an item whose model the bounds table holds in another kit names that kit, unless the
    /// caller names one.
    /// </summary>
    public string FileKit { get; init; } = DecorJsonWriter.ForestKit;

    /// <summary>The terrain's height at a piece-local position.</summary>
    public float Height(float x, float z) => _terrain.Height(x, z);

    /// <summary>The model's kit box (the bounds table's).</summary>
    public ModelBounds Box(string model) =>
        _bounds.TryGetValue(model, out ModelBounds? box) ? box : throw new InvalidOperationException($"{Name}: the kit's bounds table has no model '{model}'");

    /// <summary>
    /// The item standing on the terrain at (x, z), its y the terrain's height there less <paramref name="sink" />,
    /// unless its kit box, scaled and turned, would cross the cell's edge (then nothing is added).
    /// </summary>
    public bool TryAdd(string model, float x, float z, int yaw, float scale, float sink = 0f, string? kit = null)
    {
        x = Round(x);
        z = Round(z);
        return TryAddAt(model, x, _terrain.Height(x, z) - sink, z, yaw, scale, kit);
    }

    /// <summary>
    /// The item at a given y (on a ledge, a lip or a face), unless its box would cross the cell's edge.
    /// </summary>
    public bool TryAddAt(string model, float x, float y, float z, int yaw, float scale, string? kit = null)
    {
        ModelBounds box = Box(model);
        kit ??= box.Kit == FileKit ? null : box.Kit;
        var item = new DecorItem("", model, Round(x), Round(y), Round(z), Yaw.Normal(yaw), Round(scale), kit);
        if (!SceneryComposer.FitsInCell(item, _bounds)) return false;
        Items.Add(item);
        return true;
    }

    /// <summary>Whether a spot of the radius is clear of every spot taken so far.</summary>
    public bool Open(float x, float z, float radius) => _taken.All(t => Distance(x, z, t.X, t.Z) >= t.Radius + radius);

    /// <summary>Takes a spot, so later spaced items keep off it.</summary>
    public void Take(float x, float z, float radius) => _taken.Add((x, z, radius));

    /// <summary>
    /// The item on the terrain at an open spot of the radius, which it then takes; a random yaw unless given.
    /// </summary>
    public bool TryPlace(string model, float x, float z, float radius, float scale, float sink = 0f, int? yaw = null, string? kit = null)
    {
        x = Round(x);
        z = Round(z);
        if (!Open(x, z, radius) || !TryAdd(model, x, z, yaw ?? Rng.Next(360), scale, sink, kit)) return false;
        Take(x, z, radius);
        return true;
    }

    /// <summary>
    /// An ancient tree (<c>avalon_tree_ancient_&lt;variant&gt;</c>) at an open spot: its trunk takes its spot and its
    /// crown is kept for the kit trees under it (<see cref="TryTree" />). Near the cell's edge a crown's box, turned,
    /// may not fit at the yaw given: then it is turned near a quarter turn, where its square box is narrowest.
    /// </summary>
    public bool TryAncient(int variant, float x, float z, int yaw, float scale = 1f)
    {
        float trunk = s_ancientTrunk[variant - 1] * scale + 0.4f;
        string model = $"avalon_tree_ancient_{variant}";
        bool placed = TryPlace(model, x, z, trunk, scale, sink: 0.1f, yaw: yaw);
        for (int attempt = 0; !placed && attempt < 12; attempt++)
            placed = TryPlace(model, x, z, trunk, scale, sink: 0.1f, yaw: Yaw.Normal(90 * Rng.Next(4) + Rng.Next(-12, 13)));
        if (!placed) return false;
        _crowns.Add((Round(x), Round(z), AncientCrown * scale, Height(x, z) - 0.1f + s_ancientCrownBase[variant - 1] * scale));
        return true;
    }

    /// <summary>
    /// A kit tree of the species at an open spot (its trunk and a little more), one of its five models drawn; under an
    /// ancient crown it is cut to keep its top <see cref="CrownHeadroom" /> below the crown's base, and left out when
    /// that leaves it under <see cref="MinTreeScale" />.
    /// </summary>
    public bool TryTree(TreeSpecies species, float x, float z, float scale, float sink = 0.1f)
    {
        string model = ForestDresser.TreeModel(species, Rng);
        float ground = Height(x, z) - sink;
        float reach = Box(model).MaxX;
        foreach ((float cx, float cz, float radius, float crownBase) in _crowns)
        {
            if (Distance(x, z, cx, cz) < radius + reach * scale)
                scale = Math.Min(scale, MathF.Floor((crownBase - CrownHeadroom - ground) / Box(model).MaxY * 20f) / 20f);
        }
        if (scale < MinTreeScale - 0.001f) return false;
        return TryPlace(model, x, z, ForestDresser.TrunkRadius(species) * scale + 0.8f, scale, sink);
    }

    /// <summary>
    /// The visual barrier along each of the <paramref name="sides" /> (spec: a continuous barrier in every forest
    /// piece's 6 m edge band): two staggered rows of kit trees of the mix (never a twisted one, a dead one less often)
    /// at no more than the tree walls' scale (<see cref="BandTreeMinScale" /> to <see cref="BandTreeMaxScale" />),
    /// their trunks <see cref="TrunkFrom" /> to <see cref="TrunkTo" /> in; undergrowth from
    /// <see cref="UndergrowthFrom" /> to <see cref="UndergrowthTo" /> in; then, on any 3 m run of an edge still
    /// without a barrier item (SceneryRules.IsBarrier) within the band, a fern, a dark hedge or a bramble. Nothing
    /// stands where <paramref name="keepClear" /> holds, and no tree where <paramref name="keepTreesClear" /> does (a
    /// feature the trees would hide or crowd); a run that cannot be filled is refused (InvalidOperationException), as
    /// the scenery rules would refuse the piece.
    /// </summary>
    public void BarrierBand(TreeMix mix, IReadOnlyList<Side> sides, Func<float, float, bool>? keepClear = null, Func<float, float, bool>? keepTreesClear = null)
    {
        keepClear ??= static (_, _) => false;
        keepTreesClear ??= static (_, _) => false;
        foreach (Side side in sides)
        {
            for (int row = 0; row < 2; row++)
            {
                for (float along = 2.6f + row * 3.6f + Uniform(0f, 2f); along < Cell - 2.6f; along += Uniform(BandTreeMinSpacing, BandTreeMaxSpacing))
                {
                    float depth = row == 0 ? Uniform(TrunkFrom, 4.7f) : Uniform(5f, TrunkTo);
                    (float x, float z) = Local(side, along, depth);
                    if (keepClear(x, z) || keepTreesClear(x, z)) continue;
                    TreeSpecies species = BandSpecies(mix);
                    float max = species == TreeSpecies.DeadTree ? Math.Min(BandTreeMaxScale, ForestDresser.WallDeadTreeMaxScale) : BandTreeMaxScale;
                    // Near a corner a wide crown would cross the cell's edge: smaller, down to the least.
                    for (float scale = Round(Uniform(BandTreeMinScale, max), 0.05f); scale >= MinTreeScale - 0.001f; scale -= 0.1f)
                    {
                        if (TryTree(species, x, z, scale)) break;
                    }
                }
            }
        }

        int count = (int)MathF.Round(sides.Count * Cell * (UndergrowthTo - UndergrowthFrom) / 100f * BandUndergrowthPer100);
        for (int placed = 0, attempts = 0; placed < count && attempts < count * 30; attempts++)
        {
            Side side = sides[Rng.Next(sides.Count)];
            (float x, float z) = Local(side, Uniform(0.4f, Cell - 0.4f), Uniform(UndergrowthFrom, UndergrowthTo));
            if (keepClear(x, z)) continue;
            string model = PickWeighted(s_bandUndergrowth);
            float scale = Uniform(0.85f, 1.35f);
            if (TryPlace(model, x, z, Spacing(model, scale), scale)) placed++;
        }

        foreach (Side side in sides)
        {
            for (int run = 0; run < SceneryRules.BarrierRuns; run++)
            {
                float from = run * SceneryRules.BarrierSpacing, to = from + SceneryRules.BarrierSpacing;
                bool held = Holds(side, from, to);
                for (int attempt = 0; !held && attempt < 80; attempt++)
                {
                    (float x, float z) = Local(side, Uniform(from + 0.3f, to - 0.3f), Uniform(0.8f, 4.5f));
                    if (!keepClear(x, z)) held = TryAdd(s_gapFill[Rng.Next(s_gapFill.Length)], x, z, Rng.Next(360), Uniform(0.8f, 1.1f));
                }
                if (!held)
                {
                    throw new InvalidOperationException(
                        $"{Name}: no barrier fits along the {side} edge between {ForestText.N(from)} and {ForestText.N(to)} m");
                }
            }
        }
    }

    /// <summary>
    /// The darker understorey (ferns, big plants, dark hedges, brambles) at up to <paramref name="count" /> open spots
    /// where <paramref name="where" /> holds.
    /// </summary>
    public int Understorey(int count, Func<float, float, bool> where, float minScale = 0.85f, float maxScale = 1.4f) =>
        Scatter(s_understorey, count, minScale, maxScale, where);

    /// <summary>
    /// Ground cover (short grass, clover, small plants, pebbles, mushrooms) at up to <paramref name="count" /> open
    /// spots where <paramref name="where" /> holds.
    /// </summary>
    public int GroundCover(int count, Func<float, float, bool> where) => Scatter(s_groundCover, count, 0.7f, 1.1f, where, spread: 0.25f);

    /// <summary>
    /// Mossy stones (avalon_rock_mossy_*), sunk a little, at up to <paramref name="count" /> open spots where
    /// <paramref name="where" /> holds.
    /// </summary>
    public int MossyRocks(int count, Func<float, float, bool> where, float minScale = 0.7f, float maxScale = 1.3f) =>
        Scatter([("avalon_rock_mossy_1", 1), ("avalon_rock_mossy_2", 1), ("avalon_rock_mossy_3", 1)], count, minScale, maxScale, where, sink: 0.05f, spread: 0.45f);

    /// <summary>
    /// Models drawn by weight at up to <paramref name="count" /> open spots where <paramref name="where" /> holds,
    /// each at a scale in the range, spaced by <paramref name="spread" /> of its box's mean width.
    /// </summary>
    public int Scatter(
        IReadOnlyList<(string Model, int Weight)> models, int count, float minScale, float maxScale, Func<float, float, bool> where,
        float sink = 0f, float spread = 0.3f)
    {
        int placed = 0;
        for (int attempts = 0; placed < count && attempts < count * 40; attempts++)
        {
            float x = Uniform(0.3f, Cell - 0.3f), z = Uniform(0.3f, Cell - 0.3f);
            if (!where(x, z)) continue;
            string model = PickWeighted(models);
            float scale = Uniform(minScale, maxScale);
            if (TryPlace(model, x, z, Spacing(model, scale, spread), scale, sink)) placed++;
        }
        return placed;
    }

    /// <summary>
    /// The spacing radius a model keeps at a scale: <paramref name="spread" /> of its box's mean width.
    /// </summary>
    public float Spacing(string model, float scale, float spread = 0.3f)
    {
        ModelBounds box = Box(model);
        return spread * ((box.MaxX - box.MinX) + (box.MaxZ - box.MinZ)) / 2f * scale;
    }

    public float Uniform(float min, float max) => min + (float)Rng.NextDouble() * (max - min);

    public string Pick(IReadOnlyList<string> models) => models[Rng.Next(models.Count)];

    public string PickWeighted(IReadOnlyList<(string Model, int Weight)> models)
    {
        int roll = Rng.Next(models.Sum(m => m.Weight));
        foreach ((string model, int weight) in models)
        {
            if (roll < weight) return model;
            roll -= weight;
        }
        return models[^1].Model;
    }

    /// <summary>
    /// The whole-degree yaw whose forward (a model's +Z) points nearest the direction (Yaw's table, so every machine
    /// agrees).
    /// </summary>
    public static int YawToward(float x, float z)
    {
        int best = 0;
        float most = float.MinValue;
        for (int d = 0; d < 360; d++)
        {
            (float sin, float cos) = Yaw.Of(d);
            float dot = sin * x + cos * z;
            if (dot > most) (best, most) = (d, dot);
        }
        return best;
    }

    /// <summary>
    /// A point <paramref name="depth" /> in from the side's edge, <paramref name="along" /> it (x for N and S, z for E
    /// and W).
    /// </summary>
    public static (float X, float Z) Local(Side side, float along, float depth) => side switch
    {
        Side.N => (along, Cell - depth),
        Side.E => (Cell - depth, along),
        Side.S => (along, depth),
        _ => (depth, along),
    };

    public static float Distance(float x0, float z0, float x1, float z1) => MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));

    public static float Round(float value, float step = 0.01f) => MathF.Round(value / step) * step;

    /// <summary>
    /// A species of the mix for the band: never a twisted tree, a dead one kept less often (the tree walls' share).
    /// </summary>
    private TreeSpecies BandSpecies(TreeMix mix)
    {
        TreeSpecies species = ForestDresser.PickSpecies(mix, Rng);
        if (species == TreeSpecies.DeadTree && Rng.NextDouble() >= ForestDresser.WallDeadTreeShare) species = ForestDresser.PickSpecies(mix, Rng);
        return species == TreeSpecies.TwistedTree ? TreeSpecies.CommonTree : species;
    }

    /// <summary>Whether an item holds the run of the side's edge, as the scenery rules check it.</summary>
    private bool Holds(Side side, float from, float to) =>
        Items.Any(item => SceneryRules.HoldsRun(item, SceneryComposer.TurnedBox(item, Box(item.Model)), side, from, to));
}
