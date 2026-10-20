using Avalon.World.ChunkLayouts;
using static Avalon.ChunkGen.SceneryShapes;

namespace Avalon.ChunkGen;

/// <summary>
/// Glimmerdell's outskirts (map 1, scenery ring #1110): one scene composed in world metres over x and z -30 to 120
/// round the walled town (x and z 0 to 60), cut by SceneryComposer into the 21 ring cells (<c>town_ring_&lt;gx&gt;_&lt;gz&gt;</c>).
/// The ground is one function sampled per cell, so every seam meets itself; it lies at y 0 along the town's walls and
/// rises gently outward, a hill under the windmill, steps at the rocky terraces, a valley under the stream. Every
/// building and landmark stands whole inside one cell. The decor file's kit is the town kit; the trees, grass, bushes
/// and flowers are the nature kit's bright models, the dark forest's tree line the forest kit's.
/// <list type="bullet">
/// <item>North: the road from the gate winding north to the map's edge, cottages, a well and carts outside the gate, a
/// lane east along the walls to the bridge; hedged and fenced fields beyond (wheat, vegetables, a ploughed field, hay
/// meadows) with scarecrows, haystacks and bales; scattered trees, then the dark forest from about z 97 (92 in the east).</item>
/// <item>East: the stream down the wall to the map's south edge, the watermill with its wheel in the water, reeds and
/// stones on the banks, the lane south past the mill, the orchard on its slope, the rocky terraces, the windmill on its
/// hill with a barn.</item>
/// <item>North-east: the stream coming down from the north-east hills, the lane crossing it on the stone bridge, the
/// wayside shrine at the bridge's foot and a ring of standing stones about 15 m out from the town's corner.</item>
/// <item>South and west: the backs of houses, their gardens, sheds, wood piles, washing lines, compost and fences,
/// kept low near the wall, between the camera and the player.</item>
/// </list>
/// Every draw comes from one random stream seeded by the prefix, so a rerun writes the same files on any machine (only
/// arithmetic, square roots and the yaw table: no trigonometry or powers). The ground is in TownScenery.Ground.cs, the
/// per-cell dressing and the rules it holds to in TownScenery.Scene.cs.
/// </summary>
public static partial class TownScenery
{
    public const ushort MapId = ChunkFiles.TownMapId;

    private const string Prefix = "town_ring";

    private const float Cell = ChunkPiece.CellSize;

    /// <summary>The terraces' risers, how high each steps the land, and how far along z their rock faces run.</summary>
    private const int Risers = 3;

    private const float RiserHeight = 1.1f, TerraceSouth = -23f, TerraceNorth = 53.5f;

    /// <summary>The tree clumps on the terraces' steps.</summary>
    private const int TerraceClumps = 15;

    /// <summary>The town's square (x and z) and the ring's outer square, in world metres.</summary>
    private const float TownMax = 60f, RingMin = -30f, RingMax = 120f;

    private const string Meadow = "meadow", Dirt = "dirt_road", Soil = "tilled_soil", Crop = "wheat_field", Floor = "forest_floor",
        Rock = "cliff_rock";

    private const string Bright = DecorJsonWriter.NatureBrightKit, Dark = DecorJsonWriter.ForestKit;

    private static readonly int s_seed = ForestDresser.StableSeed(Prefix);

    // ---- the plan (world metres, +X east, +Z north) ----

    /// <summary>
    /// The stream's course, upstream first: down from the north-east hills, bending south under the bridge and running
    /// along the east wall to a still pool short of the map's south edge, where the land falls away.
    /// </summary>
    private static readonly (float X, float Z)[] s_stream =
    [
        (124f, 104.5f), (116f, 101.5f), (108f, 97.5f), (100f, 93f), (92f, 88f), (85f, 83.5f), (79f, 79.5f), (74f, 75.5f), (70.4f, 71.8f),
        (67.6f, 68.6f), (66.2f, 66f), (65.9f, 62.5f), (65.4f, 58.6f), (64.9f, 54f), (64.9f, 44f), (64.6f, 34f), (64.9f, 24f), (64.6f, 14f),
        (65f, 4f), (65.3f, -6f), (65.2f, -13.5f), (65.5f, -18.2f),
    ];

    /// <summary>The still pool the stream ends in: its middle and radius.</summary>
    private const float PoolX = 65.6f, PoolZ = -18.6f, PoolRadius = 3.2f;

    /// <summary>The stream's half-width (3.4 m wide) and how deep its bed lies under the water at its middle.</summary>
    private const float StreamHalf = 1.7f, StreamDepth = 0.55f;

    /// <summary>The bank's run from the water's edge up to the land around, and the bare earth along its foot.</summary>
    private const float BankRun = 3.6f, BankEarth = 0.9f;

    /// <summary>
    /// How far the surfaces laid over the ground lie above it: the dirt (roads, lanes, the stream's banks) over the
    /// forest floor's fringe where the two cross.
    /// </summary>
    private const float DirtLift = 0.06f, FloorLift = 0.03f;

    /// <summary>The water lies this far under the land at the stream's course; beside the town no higher than <see cref="DellWater" />.</summary>
    private const float WaterBelow = 0.45f, DellWater = -0.45f;

    /// <summary>The least drop of the water per metre of its course (ScenerySurface's 0.5 %, and a little more for the rounding).</summary>
    private const float WaterSlope = 0.0053f;

    /// <summary>
    /// The stone bridge: across the stream at x 66.05, just where it bends away east, its road along x (a quarter turn),
    /// the lane on z 66; its 12 m length runs from x 60.05, inside cell (2, 2), and its far corner (x + z about 140) lies
    /// inside the north-east corner's game shot.
    /// </summary>
    private const float BridgeX = 66.05f, LaneZ = 66f;

    /// <summary>The lane from the road east along the fields and down to the bridge's west end.</summary>
    private static readonly (float X, float Z)[] s_laneWest =
    [
        (14f, 76.5f), (40f, 76.5f), (50f, 75.8f), (55.5f, 72.5f), (58.6f, 68.4f), (BridgeX - 6f, LaneZ),
    ];

    /// <summary>The road from the gate north to the map's edge, 4 m wide.</summary>
    private static readonly (float X, float Z)[] s_road =
    [
        (15f, 59.5f), (15f, 70f), (15.6f, 74f), (17.3f, 80f), (18.5f, 86f), (19f, 92f), (19.6f, 98f), (21f, 104f), (22.4f, 110f),
        (23.2f, 116f), (23.5f, 121f),
    ];

    /// <summary>The lane from the bridge's east end east and south past the mill and the orchard, ending short of the south edge.</summary>
    private static readonly (float X, float Z)[] s_laneSouth =
    [
        (BridgeX + 6f, LaneZ), (75.5f, 64.8f), (78.2f, 62f), (79.4f, 57f), (78.9f, 46f), (78.4f, 32f), (78f, 18f), (77.6f, 4f),
        (77.3f, -10f), (77.1f, -17.5f),
    ];

    /// <summary>The track up the windmill's hill from the lane.</summary>
    private static readonly (float X, float Z)[] s_millTrack = [(78.2f, 62.5f), (86f, 65f), (93f, 69f), (99f, 72.5f), (101.5f, 73.5f)];

    /// <summary>The mill's yard track, from the lane west to its door.</summary>
    private static readonly (float X, float Z)[] s_millYard = [(78.5f, 35.5f), (74f, 35.5f), (70f, 36f)];

    /// <summary>The watermill: the mill house's anchor (its door to the south) and its wheel in the stream.</summary>
    private const float MillX = 71f, MillZ = 42f, WheelX = 65.2f;

    /// <summary>
    /// The shrine at the bridge's east foot, the standing stones' ring south of it on the stream's east bank (13 m from the
    /// town's corner), the windmill on its hill.
    /// </summary>
    private const float StonesX = 72f, StonesZ = 55f, StonesRadius = 3.3f, ShrineX = 70.4f, ShrineZ = 62.4f, WindmillX = 104f, WindmillZ = 76f;

    /// <summary>The windmill's scale: it stands taller than the barn below it.</summary>
    private const float WindmillScale = 1.3f;

    /// <summary>
    /// The fields: whole metres, kept off the cell seams (a stand or a fence on a seam would cross a cell's edge); the
    /// crop its kind grows.
    /// </summary>
    private static readonly Field[] s_fields =
    [
        new(FieldKind.Wheat, -27f, -4f, 80f, 88f),
        new(FieldKind.Vegetables, 2f, 12f, 80f, 88f),
        new(FieldKind.Wheat, 22f, 28f, 81f, 88f),
        new(FieldKind.Wheat, 33f, 49f, 80f, 88f),
        new(FieldKind.Ploughed, 51f, 58f, 80f, 88f),
        new(FieldKind.Vegetables, 33f, 50f, 64f, 72f),
        new(FieldKind.Hay, 82f, 89f, 70f, 76f),
        new(FieldKind.Wheat, -27f, -9f, 92.5f, 96f),
        new(FieldKind.Hay, -6f, 13f, 92.5f, 96f),
        new(FieldKind.Wheat, 33f, 55f, 92.5f, 96f),
    ];

    /// <summary>The town's outlying buildings: recipe, anchor, quarter turns (ChunkRotation's sense).</summary>
    private static readonly (string Recipe, float X, float Z, int Rotation)[] s_buildings =
    [
        // Outside the gate, their doors toward the road.
        ("cottage_low", 5.6f, 67.8f, 3), ("cottage_brick", 25.4f, 68.4f, 1),
        // The watermill, its door to the south, its wheel side on the stream.
        ("mill_house", MillX, MillZ, 0),
        // The barn below the windmill.
        ("barn", 98f, 64.5f, 0),
        // South and west: their backs to the wall, their gardens between.
        ("house_a", 8.6f, -16f, 0), ("cottage_brick", 22.4f, -15.4f, 0), ("cottage_low", 38f, -16f, 0), ("house_b", 52f, -15.6f, 0),
        ("house_b", -15.8f, 8.6f, 1), ("cottage_low", -16f, 22.6f, 1), ("house_a", -16f, 38f, 1), ("cottage_brick", -15.4f, 52f, 1),
        ("barn", -19.5f, -20f, 1),
    ];

    private enum FieldKind { Wheat, Vegetables, Ploughed, Hay }

    private sealed record Field(FieldKind Kind, float MinX, float MaxX, float MinZ, float MaxZ)
    {
        public bool Holds(float x, float z, float margin = 0f) =>
            x > MinX - margin && x < MaxX + margin && z > MinZ - margin && z < MaxZ + margin;
    }

    /// <summary>
    /// The 21 town ring pieces: the scene's ground and water cut at the cell edges, each item in the cell holding it.
    /// </summary>
    public static IReadOnlyList<SceneryPiece> Pieces()
    {
        IReadOnlyDictionary<string, ModelBounds> kit = KitBounds.Load();
        IReadOnlyList<(int X, int Z)> cells = SceneryRing.Cells(SceneryRules.TownCells);
        List<PlacedBuilding> buildings = [.. s_buildings.Select((b, i) => PlacedBuilding.Of(b.Recipe, $"{b.Recipe}_{i + 1}", b.X, b.Z, b.Rotation))];
        var ground = new Ground(buildings);
        var terrain = new SceneryTerrain(ground.Height, ground.Material)
        {
            Surfaces = [ground.Water, ground.Pool, ground.Overlay, ground.Fringe],
            Walls = ground.Walls,
        };
        var scene = new Scene(terrain, ground, kit, cells);

        Buildings(scene, buildings);
        Crossing(scene);
        Gate(scene);
        Fields(scene);
        Stream(scene);
        Mill(scene);
        Orchard(scene);
        Terraces(scene);
        WindmillHill(scene);
        Gardens(scene);
        Trees(scene);
        Forest(scene);
        Verge(scene);

        return SceneryComposer.Cut(Prefix, cells, terrain, scene.Items(), DecorJsonWriter.TownKit, (_, piece) => piece with { Family = "town" });
    }

    // ---- buildings ----

    /// <summary>A recipe put at its anchor: its decor and the footprint its walls cover (its box proxies; a roof may overhang it).</summary>
    private sealed record PlacedBuilding(string Name, IReadOnlyList<DecorItem> Decor, float MinX, float MaxX, float MinZ, float MaxZ)
    {
        public static PlacedBuilding Of(string recipe, string name, float x, float z, int rotation)
        {
            (IReadOnlyList<TownPiece> proxies, IReadOnlyList<DecorItem> decor) = TownBuilding.Place(BuildingRecipe.Load(recipe), name, x, z, rotation);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (BoxPiece b in proxies.OfType<BoxPiece>())
                (minX, maxX, minZ, maxZ) = (Math.Min(minX, b.MinX), Math.Max(maxX, b.MaxX), Math.Min(minZ, b.MinZ), Math.Max(maxZ, b.MaxZ));
            if (minX > maxX) throw new InvalidOperationException($"{name}: the recipe {recipe} has no box proxy to seat it on");
            return new PlacedBuilding(name, decor, minX, maxX, minZ, maxZ);
        }

        public float X => (MinX + MaxX) / 2f;

        public float Z => (MinZ + MaxZ) / 2f;

        /// <summary>Metres from a point to the footprint, 0 inside it.</summary>
        public float Distance(float x, float z) => BoxDistance(x, z, MinX, MaxX, MinZ, MaxZ);
    }

    /// <summary>Every building's kit pieces on its level pad, each refused piece a refusal of the scene.</summary>
    private static void Buildings(Scene scene, IReadOnlyList<PlacedBuilding> buildings)
    {
        foreach (PlacedBuilding building in buildings)
        {
            float level = scene.Ground.PadLevel(building) - 0.04f;
            foreach (DecorItem piece in building.Decor)
            {
                if (!scene.AddAt(piece.Model, piece.X, level + piece.Y, piece.Z, piece.Yaw, 1f, DecorJsonWriter.TownKit))
                    throw new InvalidOperationException($"{Prefix}: {building.Name}'s {piece.Model} at ({ForestText.N(piece.X)}, {ForestText.N(piece.Z)}) does not fit");
            }
        }
    }

    // ---- the north-east: the bridge, the shrine and the standing stones ----

    /// <summary>
    /// The stone bridge over the stream where the lane crosses it, a quarter turn so its road runs along x; the wayside
    /// shrine at its east foot facing the town; the standing stones in a ring on the rise beyond, each broad face toward
    /// the ring's middle, a low stone at its heart; old trees round the meeting place, further out, so none hides it
    /// from the game camera.
    /// </summary>
    private static void Crossing(Scene scene)
    {
        Require(scene.AddAt("avalon_stone_bridge", BridgeX, scene.Ground.BridgeLevel, LaneZ, 90, 1f, DecorJsonWriter.TownKit), "the bridge");
        Require(scene.Place("avalon_wayside_shrine", ShrineX, ShrineZ, 1.1f, 225, 1f, 0.05f), "the shrine");
        foreach ((string model, float dx, float dz, float scale) in new[]
        {
            ("Flower_3_Group", 1.1f, -0.6f, 0.8f), ("Flower_4_Group", -0.8f, -1.1f, 0.75f), ("Bush_Common_Flowers", 1.4f, 1.2f, 0.7f),
            ("Flower_3_Group", -1.3f, 0.2f, 0.7f), ("Grass_Wispy_Short", 0.2f, -1.5f, 0.6f),
        })
        {
            scene.Add(model, ShrineX + dx, ShrineZ + dz, scene.Rng.Next(360), scale);
        }

        // Eight stones round the ring, their faces toward its middle, a gap on its south side where the ninth would stand.
        string[] stones = ["avalon_standing_stone_3", "avalon_standing_stone_1", "avalon_standing_stone_2"];
        for (int i = 0; i < 9; i++)
        {
            if (i == 4) continue;
            int bearing = 40 * i + scene.Rng.Next(-6, 7);
            (float sin, float cos) = Yaw.Of(bearing);
            float r = StonesRadius + scene.Uniform(-0.25f, 0.25f), x = StonesX + sin * r, z = StonesZ + cos * r;
            int yaw = SceneryDresser.YawToward(StonesX - x, StonesZ - z) + scene.Rng.Next(-8, 9);
            Require(scene.Place(stones[i % 3], x, z, 0.6f, yaw, scene.Uniform(0.88f, 1.12f), 0f), "a standing stone");
        }
        Require(scene.Place("avalon_ruin_pillar_2", StonesX, StonesZ, 1f, 25, 0.9f, 0.05f, Dark), "the heart stone");
        for (int i = 0; i < 60; i++)
        {
            (float sin, float cos) = Yaw.Of(scene.Rng.Next(360));
            float r = scene.Uniform(1.2f, StonesRadius + 2.5f);
            scene.Add(scene.Pick(["Grass_Wispy_Short", "Grass_Common_Short", "Grass_Wispy_Short", "Grass_Wispy_Tall"]),
                StonesX + sin * r, StonesZ + cos * r, scene.Rng.Next(360), scene.Uniform(0.45f, 0.75f));
        }

        // Old trees beyond the crossing, as the game camera looks (north-east).
        foreach ((TreeSpecies species, float x, float z, float scale) in new[]
        {
            (TreeSpecies.CommonTree, 84.5f, 55.5f, 1.3f), (TreeSpecies.CommonTree, 76f, 70.5f, 1.25f), (TreeSpecies.Pine, 85.6f, 78.6f, 1.2f),
        })
        {
            Require(scene.Tree(species, x, z, scale, Bright), $"the old tree at ({ForestText.N(x)}, {ForestText.N(z)})");
            scene.Cluster(x, z, ["avalon_hedge_green", "Fern_1", "Grass_Common_Tall", "Bush_Common_Flowers"], 2.4f, 0.75f, 1.05f);
        }
    }

    // ---- the north: the gate, the cottages and the fields ----

    /// <summary>
    /// Just outside the gate: carts, a well, wood piles, barrels and crates by the cottages, a bench, fenced yards, a
    /// cabbage bed, a lantern post by the road.
    /// </summary>
    private static void Gate(Scene scene)
    {
        foreach ((string model, float x, float z, int yaw, float scale) in new[]
        {
            ("avalon_well", 20.4f, 65.6f, 15, 1f), ("Prop_Wagon", 10.6f, 63.6f, 20, 1f), ("Prop_Wagon", 21.6f, 77.4f, 262, 1f),
            ("avalon_wood_pile", 11.3f, 70.8f, 90, 1f), ("avalon_wood_pile", 28.4f, 63.4f, 0, 1f), ("avalon_bench", 10.7f, 68.6f, 270, 1f),
            ("avalon_barrel", 11.2f, 66.2f, 0, 1f), ("avalon_barrel", 11.6f, 67.1f, 40, 1f), ("avalon_barrel_broken", 23.8f, 63.2f, 70, 1f),
            ("Prop_Crate", 25.1f, 62.6f, 15, 0.9f), ("Prop_Crate", 26f, 63.6f, 0, 0.8f), ("avalon_lamp_post", 18.4f, 62.2f, 0, 1f),
            ("avalon_notice_board", 10.4f, 61.2f, 180, 1f),
        })
        {
            Require(scene.Place(model, x, z, scene.Spacing(model, scale, 0.3f), yaw, scale, 0f), $"the gate's {model} at ({ForestText.N(x)}, {ForestText.N(z)})");
        }

        // A cabbage bed before cottage_low, a washing line east of cottage_brick.
        foreach (float z in new[] { 61.8f, 62.6f, 63.4f })
        {
            for (float x = 2.2f; x < 7.4f; x += scene.Uniform(0.45f, 0.55f))
                scene.Add(scene.Pick(["avalon_cabbage_1", "avalon_cabbage_2", "avalon_cabbage_3"]), x, z + scene.Uniform(-0.06f, 0.06f), scene.Rng.Next(360), scene.Uniform(0.9f, 1.1f));
        }
        Require(scene.Place("avalon_washing_line", 31.2f, 66f, 1f, 90, 1f, 0f), "the cottages' washing line");
    }

    /// <summary>
    /// The fields between the lane and the forest: each crop in its rows (wheat stands 1 m along a row, rows 0.95 m
    /// apart; cabbages and carrots in alternate rows), fenced or hedged round, a scarecrow in the wheat, haystacks on
    /// the hay meadows and bales on the ploughed field; hedgerows along the lane's north side and between the fields.
    /// </summary>
    private static void Fields(Scene scene)
    {
        foreach (Field field in s_fields)
        {
            switch (field.Kind)
            {
                case FieldKind.Wheat:
                    for (float z = field.MinZ + 0.5f; z <= field.MaxZ - 0.45f; z += 0.95f)
                    {
                        for (float x = field.MinX + 0.5f; x <= field.MaxX - 0.5f; x += 1f)
                        {
                            scene.Add(scene.PickWeighted([("avalon_wheat_1", 1), ("avalon_wheat_2", 2), ("avalon_wheat_3", 1)]),
                                x + scene.Uniform(-0.13f, 0.13f), z + scene.Uniform(-0.13f, 0.13f), scene.Rng.Next(360), scene.Uniform(0.94f, 1.06f), 0.02f);
                        }
                    }
                    if (field.MaxX - field.MinX > 12f)
                    {
                        float sx = field.MinX + (field.MaxX - field.MinX) * scene.Uniform(0.3f, 0.7f), sz = (field.MinZ + field.MaxZ) / 2f;
                        scene.Remove(sx, sz, 0.9f);
                        scene.Place(scene.Pick(["avalon_scarecrow_1", "avalon_scarecrow_2"]), sx, sz, 0.8f, 200 + scene.Rng.Next(-20, 21), 1f, 0.05f);
                    }
                    break;
                case FieldKind.Vegetables:
                    int row = 0;
                    for (float z = field.MinZ + 0.45f; z <= field.MaxZ - 0.3f; z += 0.75f, row++)
                    {
                        bool cabbage = row % 3 != 1;
                        for (float x = field.MinX + 0.35f; x <= field.MaxX - 0.3f; x += cabbage ? 0.5f : 0.34f)
                        {
                            if (scene.Rng.NextDouble() < 0.05) continue;
                            string model = cabbage ? scene.Pick(["avalon_cabbage_1", "avalon_cabbage_2", "avalon_cabbage_3"]) : scene.Pick(["avalon_carrot_1", "avalon_carrot_2", "avalon_carrot_3"]);
                            scene.Add(model, x + scene.Uniform(-0.04f, 0.04f), z + scene.Uniform(-0.05f, 0.05f), scene.Rng.Next(360), scene.Uniform(0.88f, 1.12f));
                        }
                    }
                    break;
                case FieldKind.Ploughed:
                    for (int i = 0; i < 5; i++)
                    {
                        float x = scene.Uniform(field.MinX + 1f, field.MaxX - 1f), z = scene.Uniform(field.MinZ + 1f, field.MaxZ - 1f);
                        scene.Place(scene.Pick(["avalon_hay_bale_1", "avalon_hay_bale_2"]), x, z, 1f, scene.Rng.Next(360), 1f, 0.03f);
                    }
                    break;
                case FieldKind.Hay:
                    for (float x = field.MinX + 2.2f; x < field.MaxX - 2f; x += scene.Uniform(4.2f, 5.6f))
                    {
                        float z = scene.Uniform(field.MinZ + 1.8f, field.MaxZ - 1.8f);
                        scene.Place(scene.Pick(["avalon_haystack_1", "avalon_haystack_2"]), x, z, 1.8f, scene.Rng.Next(360), scene.Uniform(0.85f, 1f), 0.05f);
                    }
                    break;
            }

            // A fence round the crop fields, a hedge round the hay.
            float west = field.MinX - 0.6f, east = field.MaxX + 0.6f, south = field.MinZ - 0.6f, north = field.MaxZ + 0.6f;
            if (field.Kind == FieldKind.Hay)
            {
                scene.Hedge([(west, south), (east, south)], 0.25f);
                scene.Hedge([(west, north), (east, north)], 0.25f);
            }
            else
            {
                scene.FenceRun((west, north), (east, north));
                scene.FenceRun((east, north), (east, south));
                scene.FenceRun((east, south), (west, south));
                scene.FenceRun((west, south), (west, north));
            }
        }

        // Hedgerows down the cell seams' sides between the fields, and along the seam between the two tiers of fields.
        scene.Hedge([(-1.4f, 79.6f), (-1.4f, 89f)], 0.1f);
        scene.Hedge([(31.2f, 79.6f), (31.2f, 89f)], 0.15f);
        scene.Hedge([(-28.5f, 91f), (16.2f, 91f)], 0.2f);
        scene.Hedge([(23.6f, 91f), (58.5f, 91f)], 0.2f);
    }

    // ---- the east: the stream, the mill, the orchard, the terraces and the windmill ----

    /// <summary>
    /// The stream's banks: reeds and tall grass in clumps at the water's edge, never in a line, stones and pebbles among
    /// them and in the shallows; a few trees leaning over the water downstream.
    /// </summary>
    private static void Stream(Scene scene)
    {
        Ground g = scene.Ground;
        for (int i = 2; i < g.Course.Count - 2; i += scene.Rng.Next(2, 6))
        {
            (float ax, float az) = g.Course[i];
            (float bx, float bz) = g.Course[i + 1];
            float length = SceneryDresser.Distance(ax, az, bx, bz), nx = -(bz - az) / length, nz = (bx - ax) / length;
            float side = scene.Rng.Next(2) == 0 ? -1f : 1f, off = StreamHalf + scene.Uniform(-0.1f, 0.8f);
            float cx = ax + nx * side * off, cz = az + nz * side * off;
            if (SceneryDresser.Distance(cx, cz, BridgeX, LaneZ) < 7.5f || (cx > 63.5f && cx < 67.5f && cz > 38f && cz < 46f)) continue;
            int n = scene.Rng.Next(4, 11);
            for (int k = 0; k < n; k++)
            {
                float x = cx + scene.Uniform(-1.1f, 1.1f), z = cz + scene.Uniform(-1.1f, 1.1f);
                if (g.StreamDistance(x, z) < StreamHalf - 0.4f || g.InPool(x, z, 0.1f) || g.TownDistance(x, z) < 0.4f) continue;
                scene.Add(scene.PickWeighted([("Grass_Common_Tall", 6), ("Grass_Wispy_Tall", 4), ("Plant_1", 1), ("Fern_1", 1)]), x, z,
                    scene.Rng.Next(360), scene.Uniform(0.75f, 1.08f), 0.04f);
            }
            if (scene.Rng.NextDouble() < 0.5)
            {
                for (int k = scene.Rng.Next(1, 4); k > 0; k--)
                {
                    float x = cx + scene.Uniform(-1.2f, 1.2f), z = cz + scene.Uniform(-1.2f, 1.2f);
                    bool pebble = scene.Rng.NextDouble() < 0.5;
                    scene.Add(pebble ? scene.Pick(["Pebble_Round_3", "Pebble_Square_4", "Pebble_Round_1"]) : scene.Pick(["Rock_Medium_1", "Rock_Medium_2"]), x, z,
                        scene.Rng.Next(360), pebble ? scene.Uniform(1.2f, 2f) : scene.Uniform(0.22f, 0.36f), pebble ? 0.02f : 0.12f);
                }
            }
        }

        // Trees leaning over the water downstream of the mill and past the town's corner.
        foreach ((float x, float z, float scale) in new[] { (70.2f, 9f, 1.05f), (70f, -14f, 1.15f), (61f, -21f, 0.95f), (70.4f, 24.5f, 0.9f) })
            scene.Tree(TreeSpecies.CommonTree, x, z, scale, Bright);
    }

    /// <summary>
    /// The watermill: the mill house (Buildings), its wheel in the stream beside it, axle across the flow toward the house,
    /// its waterline on the water; a cart, barrels and crates in its yard, bales stacked by the lane, a lean-to shed.
    /// </summary>
    private static void Mill(Scene scene)
    {
        float water = scene.Ground.WaterLevel(WheelX, MillZ);
        Require(scene.AddAt("avalon_water_wheel", WheelX, water, MillZ, 90, 1f, DecorJsonWriter.TownKit), "the water wheel");
        scene.Take(WheelX, MillZ, 2.2f);
        foreach ((string model, float x, float z, int yaw, float scale) in new[]
        {
            ("Prop_Wagon", 76f, 39.6f, 200, 1f), ("avalon_barrel", 75.4f, 46.6f, 0, 1f), ("avalon_barrel", 76.1f, 47.4f, 30, 1f),
            ("avalon_barrel", 75.2f, 47.7f, 75, 1f), ("Prop_Crate", 67.8f, 35.8f, 10, 0.9f), ("Prop_Crate", 68.2f, 34.9f, 0, 0.8f),
            ("avalon_hay_bale_2", 75.6f, 33.2f, 90, 1f), ("avalon_hay_bale_1", 74f, 32.6f, 30, 1f), ("avalon_shed_leanto_1", 69.5f, 49.2f, 90, 1f),
            ("avalon_wood_pile", 73f, 49.2f, 0, 1f),
        })
        {
            Require(scene.Place(model, x, z, scene.Spacing(model, scale, 0.3f), yaw, scale, 0f), $"the mill's {model} at ({ForestText.N(x)}, {ForestText.N(z)})");
        }
    }

    /// <summary>
    /// The orchard on the slope east of the lane: small fruit trees in staggered rows, a low dry-stone wall along the
    /// lane at its foot with gaps where tracks run in, crates and barrels under a few trees.
    /// </summary>
    private static void Orchard(Scene scene)
    {
        int column = 0;
        for (float x = 82.2f; x < 89f; x += 3.3f, column++)
        {
            for (float z = 4.5f + (column % 2) * 2.4f; z < 57f; z += 4.8f)
            {
                if (scene.Rng.NextDouble() < 0.08) continue;
                float tx = x + scene.Uniform(-0.4f, 0.4f), tz = z + scene.Uniform(-0.4f, 0.4f);
                for (float scale = scene.Uniform(0.5f, 0.62f); scale > 0.4f; scale -= 0.05f)
                {
                    if (scene.Tree(TreeSpecies.CommonTree, tx, tz, scale, Bright, ["CommonTree_1", "CommonTree_2", "CommonTree_5"])) break;
                }
                if (scene.Rng.NextDouble() < 0.12) scene.Place("Prop_Crate", tx + 1.2f, tz - 0.6f, 0.5f, scene.Rng.Next(360), 0.75f, 0f);
            }
        }
        foreach ((float from, float to) in new[] { (1.5f, 13.5f), (16.5f, 28.5f), (31.5f, 37f), (40f, 57f) })
        {
            for (float z = from + 2f; z <= to - 2f + 0.01f; z += 4f)
                scene.AddAt("avalon_field_wall_1", 81.1f, scene.Ground.Height(81.1f, z) - 0.05f, z, 90 + scene.Rng.Next(-3, 4), 1f, DecorJsonWriter.TownKit);
        }
    }

    /// <summary>
    /// The rocky terraces east of the orchard (their faces are the ground's walls, Ground): trees in
    /// <see cref="TerraceClumps" /> clumps of three to six on the steps' flats, scrub round them; mossy stones and
    /// boulders heaped at each face's foot and a few on its lip, boulders where a face breaks at a cell's seam.
    /// </summary>
    private static void Terraces(Scene scene)
    {
        Ground g = scene.Ground;

        // The steps first: trees in clumps on each step's flat (Flat), 9 m or more apart, scrub round them. A clump counts
        // once it holds three trees; one that cannot is left with what it got and another is tried.
        List<(float X, float Z)> centres = [];
        for (int clumps = 0, tries = 0; clumps < TerraceClumps && tries < 400; tries++)
        {
            float cz = scene.Uniform(TerraceSouth + 3f, TerraceNorth - 3f);
            (float from, float to) = Flat(scene.Rng.Next(Risers + 1), cz);
            if (to - from < 2f) continue;
            float cx = scene.Uniform(from, to);
            if (g.OnRoad(cx, cz, 3f) || centres.Any(c => SceneryDresser.Distance(cx, cz, c.X, c.Z) < 9f)) continue;
            int planted = 0;
            for (int i = 0, want = scene.Rng.Next(3, 7); i < 14 && planted < want; i++)
            {
                float z = cz + scene.Uniform(-4.5f, 4.5f);
                (float x0, float x1) = Flat(Step(cx, z), z);
                float x = Math.Clamp(cx + scene.Uniform(-1.8f, 1.8f), x0, x1);
                TreeSpecies species = scene.Rng.NextDouble() < 0.65 ? TreeSpecies.Pine : TreeSpecies.CommonTree;
                if (x1 > x0 && scene.Tree(species, x, z, scene.Uniform(0.85f, 1.3f), Bright, clearance: 0.45f)) planted++;
            }
            if (planted < 3) continue;
            clumps++;
            centres.Add((cx, cz));
            scene.Cluster(cx, cz, ["avalon_hedge_green", "Fern_1", "Grass_Wispy_Tall", "avalon_hedge_dark", "Plant_1_Big"], 3f, 0.8f, 1.2f);
        }

        (string Model, int Weight)[] foot =
        [
            ("avalon_rock_mossy_1", 3), ("avalon_rock_mossy_2", 3), ("avalon_rock_mossy_3", 3), ("avalon_boulder_1", 2), ("avalon_boulder_3", 1),
            ("avalon_boulder_4", 1),
        ];
        for (int k = 0; k < Risers; k++)
        {
            // Stones heaped at the face's foot, a few on its lip; boulders where the face breaks at a cell's seam.
            for (float z = TerraceSouth + 0.5f; z < TerraceNorth; z += scene.Uniform(1.1f, 2.2f))
            {
                float x = Ground.RiserX(k, z);
                string model = scene.PickWeighted(foot);
                float scale = model.StartsWith("avalon_boulder", StringComparison.Ordinal) ? scene.Uniform(0.45f, 0.8f) : scene.Uniform(0.6f, 1.1f);
                scene.Place(model, x - scene.Uniform(0.1f, 0.8f), z, scene.Spacing(model, scale, 0.3f), scene.Rng.Next(360), scale, 0.12f, Dark);
                if (scene.Rng.NextDouble() < 0.3)
                    scene.Place(scene.Pick(["avalon_rock_mossy_1", "avalon_rock_mossy_3"]), x + scene.Uniform(1.2f, 1.6f), z, 0.6f, scene.Rng.Next(360), scene.Uniform(0.45f, 0.7f), 0.1f, Dark);
            }
            foreach (float seam in new[] { 0f, Cell })
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    bool placed = false;
                    for (int attempt = 0; !placed && attempt < 10; attempt++)
                        placed = scene.Place("avalon_boulder_4", Ground.RiserX(k, seam + side) + 0.3f, seam + side * 1.25f, 1f, scene.Rng.Next(360), scene.Uniform(0.5f, 0.62f), 0.15f, Dark);
                }
            }
        }

        // The step a point lies on: 0 west of the first riser, then one more past each.
        static int Step(float x, float z)
        {
            int step = 0;
            while (step < Risers && x > Ground.RiserX(step, z)) step++;
            return step;
        }

        // A step's flat at z, where trees stand: clear of the stones at the foot of the face east of it (1.2 m) and of
        // its crest and lip west of it (1.4 m past its riser's band, one Step wide), and inside the cells' x 90-120 by
        // a crown's reach.
        static (float From, float To) Flat(int step, float z) =>
            (step == 0 ? 93f : Ground.RiserX(step - 1, z) + 2.4f, step == Risers ? 117.4f : Ground.RiserX(step, z) - 1.2f);
    }

    /// <summary>
    /// The windmill on its hill, sails toward the town; the barn below it (Buildings), a haystack, bales and a fence along
    /// the track; flowers and rough grass on the hill.
    /// </summary>
    private static void WindmillHill(Scene scene)
    {
        Require(scene.AddAt("avalon_windmill", WindmillX, scene.Ground.Height(WindmillX, WindmillZ) - 0.1f, WindmillZ, 225, WindmillScale, DecorJsonWriter.TownKit),
            "the windmill");
        scene.Take(WindmillX, WindmillZ, 6f);
        foreach ((string model, float x, float z, int yaw) in new[]
        {
            ("avalon_haystack_2", 116.4f, 72.6f, 30), ("avalon_hay_bale_1", 105.6f, 61.4f, 10), ("avalon_hay_bale_2", 107.4f, 61.2f, 80),
            ("avalon_hay_bale_1", 96.4f, 79.4f, 120), ("Prop_Wagon", 99.2f, 69.6f, 75),
        })
        {
            Require(scene.Place(model, x, z, scene.Spacing(model, 1f, 0.3f), yaw, 1f, 0.03f), $"the hill's {model} at ({ForestText.N(x)}, {ForestText.N(z)})");
        }
        scene.FenceRun((109f, 63.6f), (118.2f, 63.6f));
        scene.FenceRun((118.2f, 63.6f), (118.2f, 70.6f));
        for (int i = 0; i < 120; i++)
        {
            (float sin, float cos) = Yaw.Of(scene.Rng.Next(360));
            float r = scene.Uniform(5.5f, 14f), x = WindmillX + sin * r, z = WindmillZ + cos * r;
            if (scene.Ground.OnRoad(x, z, 0.8f)) continue;
            scene.Add(scene.PickWeighted([("Grass_Wispy_Tall", 4), ("Flower_4_Group", 2), ("Flower_3_Group", 1), ("avalon_hedge_green", 1), ("Grass_Common_Short", 3)]),
                x, z, scene.Rng.Next(360), scene.Uniform(0.6f, 1f), 0.03f);
        }
    }

    // ---- the south and the west: the backs of the houses ----

    /// <summary>
    /// Each house's garden between its back and the wall: a vegetable bed on tilled soil, a lean-to shed, a wood pile, a
    /// washing line, a compost bin, barrels and crates, a low fence along each side; nothing taller than the wall
    /// within 4 m of it, nothing taller than 6 m within 10 m.
    /// </summary>
    private static void Gardens(Scene scene)
    {
        foreach (Garden garden in Ground.Gardens)
        {
            bool west = garden.West;
            // The fences along the garden's two sides, square to the wall.
            if (west)
            {
                scene.FenceRun((garden.MinX, garden.MinZ), (garden.MaxX - 0.6f, garden.MinZ), gaps: 0.12f);
                scene.FenceRun((garden.MinX, garden.MaxZ), (garden.MaxX - 0.6f, garden.MaxZ), gaps: 0.12f);
            }
            else
            {
                scene.FenceRun((garden.MinX, garden.MinZ), (garden.MinX, garden.MaxZ - 0.6f), gaps: 0.12f);
                scene.FenceRun((garden.MaxX, garden.MinZ), (garden.MaxX, garden.MaxZ - 0.6f), gaps: 0.12f);
            }

            // The bed's rows, along the wall.
            (float bx0, float bx1, float bz0, float bz1) = garden.Bed;
            int row = 0;
            if (west)
            {
                for (float x = bx0 + 0.4f; x <= bx1 - 0.3f; x += 0.7f, row++)
                    Row(scene, x, bz0 + 0.35f, x, bz1 - 0.3f, row);
            }
            else
            {
                for (float z = bz0 + 0.4f; z <= bz1 - 0.3f; z += 0.7f, row++)
                    Row(scene, bx0 + 0.35f, z, bx1 - 0.3f, z, row);
            }

            // The shed, the wood pile, the washing line, the compost: the shed 4 m or more from the wall.
            (float sx, float sz, int shedYaw) = garden.Shed;
            Require(scene.Place(garden.Index % 2 == 0 ? "avalon_shed_leanto_1" : "avalon_shed_leanto_2", sx, sz, 1.1f, shedYaw, 1f, 0.02f), $"garden {garden.Index}'s shed");
            (float wx, float wz, int lineYaw) = garden.Line;
            scene.Place("avalon_washing_line", wx, wz, 1.2f, lineYaw, 1f, 0f);
            (float px, float pz) = garden.Pile;
            scene.Place("avalon_wood_pile", px, pz, 0.8f, west ? 90 : 0, 1f, 0f);
            (float cx, float cz) = garden.Compost;
            scene.Place("avalon_compost", cx, cz, 0.9f, scene.Rng.Next(4) * 90 + scene.Rng.Next(-8, 9), 1f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float x = scene.Uniform(garden.MinX + 1f, garden.MaxX - 1f), z = scene.Uniform(garden.MinZ + 1f, garden.MaxZ - 1f);
                if (scene.Ground.InBed(x, z, 0.6f)) continue;
                scene.Place(scene.Pick(["avalon_barrel", "Prop_Crate", "avalon_barrel_broken", "avalon_bench"]), x, z, 0.7f, scene.Rng.Next(360), 0.9f, 0f);
            }
        }

        // The corner: an old tree with a bench under it, haystacks by the barn, a low hedge round the corner.
        Require(scene.Tree(TreeSpecies.CommonTree, -12.2f, -12.4f, 1.2f, Bright), "the corner's old tree");
        scene.Place("avalon_bench", -9.6f, -8.8f, 0.8f, 225, 1f, 0f);
        scene.Place("avalon_haystack_1", -24.6f, -8f, 1.8f, 10, 0.9f, 0.05f);
        scene.Place("avalon_haystack_2", -8.6f, -25.2f, 1.8f, 70, 0.9f, 0.05f);
        scene.Hedge([(-1.6f, -6.2f), (-6.2f, -6.2f), (-6.2f, -1.6f)], 0.2f, 0.85f);
    }

    /// <summary>One bed row: cabbages and carrots in turn, a gap here and there.</summary>
    private static void Row(Scene scene, float x0, float z0, float x1, float z1, int row)
    {
        bool cabbage = row % 2 == 0;
        float length = SceneryDresser.Distance(x0, z0, x1, z1), step = cabbage ? 0.5f : 0.34f;
        for (float t = 0f; t <= length; t += step)
        {
            if (scene.Rng.NextDouble() < 0.06) continue;
            float x = x0 + (x1 - x0) * t / length, z = z0 + (z1 - z0) * t / length;
            scene.Add(cabbage ? scene.Pick(["avalon_cabbage_1", "avalon_cabbage_2", "avalon_cabbage_3"]) : scene.Pick(["avalon_carrot_1", "avalon_carrot_2", "avalon_carrot_3"]),
                x + scene.Uniform(-0.04f, 0.04f), z + scene.Uniform(-0.04f, 0.04f), scene.Rng.Next(360), scene.Uniform(0.88f, 1.1f));
        }
    }

    // ---- trees, the forest and the grass ----

    /// <summary>
    /// Scattered trees and bushes in the open land: between the fields and the forest, behind the houses (10 m or more
    /// from the wall), down the stream's far side, round the windmill's hill; none on a road, a field, a garden or the
    /// water, none near the wall nor between the town's north-east corner and the standing stones, where the game
    /// camera looks, and none on the orchard or the terraces, which plant their own.
    /// </summary>
    private static void Trees(Scene scene)
    {
        Ground g = scene.Ground;
        for (int i = 0; i < 5000; i++)
        {
            float x = scene.Uniform(RingMin + 1f, RingMax - 1f), z = scene.Uniform(RingMin + 1f, RingMax - 1f);
            float d = g.TownDistance(x, z);
            if (d < 9f || g.InForest(x, z) || g.OnRoad(x, z, 3f) || g.InField(x, z, 2.5f) || g.InGarden(x, z, 1.5f) || g.StreamDistance(x, z) < StreamHalf + 2.5f
                || g.NearBuilding(x, z, 3.5f) || x > 89f && z < 58f
                || x > 60f && x < 80f && z > 44f && x + z < 150f)
            {
                continue;
            }
            // Thinner near the town, a wood's edge thickening toward the forest and the map's edges.
            float keep = 0.35f + 0.6f * Smooth(10f, 34f, d);
            if (scene.Rng.NextDouble() > keep) continue;
            TreeSpecies species = scene.Rng.NextDouble() < 0.72 ? TreeSpecies.CommonTree : TreeSpecies.Pine;
            if (!scene.Tree(species, x, z, scene.Uniform(0.85f, 1.3f), Bright)) continue;
            if (scene.Rng.NextDouble() < 0.6)
                scene.Cluster(x, z, ["avalon_hedge_green", "Grass_Common_Tall", "Fern_1"], 2.2f, 0.75f, 1.1f);
        }
    }

    /// <summary>
    /// The dark forest's tree line from about z 97, 92 in the east (the forest kit's darkened trees, as the forest the north road leads
    /// to looks): dense trees and undergrowth on forest floor, its fringe thinning toward the fields; none on the road
    /// or the water.
    /// </summary>
    private static void Forest(Scene scene)
    {
        Ground g = scene.Ground;
        (TreeSpecies Species, int Weight)[] mix = [(TreeSpecies.Pine, 11), (TreeSpecies.CommonTree, 8), (TreeSpecies.DeadTree, 1)];
        for (float z = 89f; z < RingMax - 0.5f; z += 2.6f)
        {
            for (float x = RingMin + 0.8f; x < RingMax - 0.5f; x += 2.8f)
            {
                float tx = x + scene.Uniform(-1.3f, 1.3f), tz = z + scene.Uniform(-1.3f, 1.3f);
                float depth = tz - g.ForestEdge(tx);
                if (depth < -1.5f || (depth < 2.5f && scene.Rng.NextDouble() < 0.35)) continue;
                if (g.OnRoad(tx, tz, 2.2f) || g.StreamDistance(tx, tz) < StreamHalf + 1.6f || g.InField(tx, tz, 2f)) continue;
                int roll = scene.Rng.Next(20), at = 0;
                TreeSpecies species = mix.First(m => (at += m.Weight) > roll).Species;
                if (species == TreeSpecies.DeadTree && depth < 8f) species = TreeSpecies.Pine;
                float scale = scene.Uniform(1.15f, 1.6f) * (1f + 0.3f * Math.Min(1f, depth / 12f)) * (species == TreeSpecies.DeadTree ? 0.7f : 1f);
                bool planted = false;
                for (int attempt = 0; !planted && attempt < 5; attempt++, scale -= 0.12f)
                    planted = scene.Tree(species, tx, tz, Math.Max(0.8f, scale), Dark);
                if (scene.Rng.NextDouble() < 0.6)
                {
                    scene.Place(scene.PickWeighted([("avalon_hedge_dark", 3), ("Fern_1", 3), ("Plant_1_Big", 2), ("avalon_bramble_1", 1)]),
                        tx + scene.Uniform(-2.5f, 2.5f), tz + scene.Uniform(-2.5f, 2.5f), 0.6f, scene.Rng.Next(360), scene.Uniform(0.85f, 1.35f), 0.03f,
                        Dark);
                }
            }
        }

        // The fringe: dark and bright bushes, ferns and brambles where the trees begin.
        for (int i = 0; i < 420; i++)
        {
            float x = scene.Uniform(RingMin + 0.5f, RingMax - 0.5f), z = g.ForestEdge(x) + scene.Uniform(-2.5f, 2f);
            if (g.OnRoad(x, z, 1.6f) || g.StreamDistance(x, z) < StreamHalf + 0.8f || g.InField(x, z, 0.8f)) continue;
            bool dark = scene.Rng.NextDouble() < 0.55;
            string model = dark ? scene.Pick(["avalon_hedge_dark", "Fern_1", "Plant_1_Big", "avalon_bramble_2"]) : scene.Pick(["avalon_hedge_green", "Fern_1", "Grass_Wispy_Tall"]);
            scene.Add(model, x, z, scene.Rng.Next(360), scene.Uniform(0.8f, 1.3f), 0.03f, dark ? Dark : null);
        }
    }

    /// <summary>
    /// The ground's grass, where every game camera looks: clumps of tall grass, nettle-like plants and wild flowers, thick
    /// at the wall's foot and thinning outward; short grass, clover and pebbles between; drifts of flowers in patches;
    /// under the forest only its own sparse cover. None on a road, the water, a field or a garden bed.
    /// </summary>
    private static void Verge(Scene scene)
    {
        Ground g = scene.Ground;
        (string Model, int Weight)[] tall = [("Grass_Wispy_Tall", 8), ("Grass_Common_Tall", 7), ("Grass_Wispy_Short", 5), ("Fern_1", 1), ("Plant_1", 1), ("Plant_7", 1)];
        (string Model, int Weight)[] low = [("Grass_Common_Short", 10), ("Grass_Wispy_Short", 9), ("Grass_Wispy_Tall", 4), ("Grass_Common_Tall", 2), ("Plant_7", 1)];
        const float Step = 0.55f;
        for (float z = RingMin + 0.3f; z < RingMax - 0.3f; z += Step)
        {
            for (float x = RingMin + 0.3f; x < RingMax - 0.3f; x += Step)
            {
                float d = g.TownDistance(x, z);
                if (d < 0.3f) continue;
                float px = x + scene.Uniform(-0.28f, 0.28f), pz = z + scene.Uniform(-0.28f, 0.28f);
                if (g.InForest(px, pz))
                {
                    if (scene.Rng.NextDouble() < 0.05)
                        scene.Add(scene.Pick(["Fern_1", "Plant_1", "Grass_Wispy_Short", "Mushroom_Common"]), px, pz, scene.Rng.Next(360), scene.Uniform(0.7f, 1.1f), 0.02f, Dark);
                    continue;
                }
                if (g.OnRoad(px, pz, 0.2f) || g.StreamDistance(px, pz) < StreamHalf + 0.25f || g.InPool(px, pz, 0.25f) || g.InField(px, pz, 0.15f)
                    || g.InBed(px, pz, 0.2f) || g.NearBuilding(px, pz, 0.2f))
                {
                    continue;
                }
                float clump = Noise(s_seed + 21, px, pz, 4.5f);
                bool foot = d < 1.6f;
                float density = foot ? 1f : d < 14f ? 0.85f : d < 30f ? 0.34f : 0.1f;
                float keep = foot ? 0.95f : density * (0.4f + 0.8f * clump);
                if (scene.Rng.NextDouble() > keep) continue;
                bool lush = foot || clump > 0.55f || g.StreamDistance(px, pz) < StreamHalf + 2.6f;
                string model = scene.PickWeighted(lush ? tall : low);
                float scale = lush ? scene.Uniform(0.6f, 1f) : scene.Uniform(0.5f, 0.85f);
                scene.AddShrinking(model, px, pz, scene.Rng.Next(360), scale, 0.02f);
            }
        }

        // Drifts of wild flowers, in patches, not dotted everywhere.
        for (int i = 0; i < 30; i++)
        {
            float cx = scene.Uniform(RingMin + 2f, RingMax - 2f), cz = scene.Uniform(RingMin + 2f, 96f);
            if (g.TownDistance(cx, cz) < 1f) continue;
            string model = scene.Pick(["Flower_3_Group", "Flower_4_Group", "Flower_3_Group"]);
            for (int k = scene.Rng.Next(5, 11); k > 0; k--)
            {
                float x = cx + scene.Uniform(-1.8f, 1.8f), z = cz + scene.Uniform(-1.8f, 1.8f);
                if (g.OnRoad(x, z, 0.3f) || g.StreamDistance(x, z) < StreamHalf + 0.5f || g.InPool(x, z, 0.5f) || g.InField(x, z, 0.3f) || g.InBed(x, z, 0.3f)
                    || g.InForest(x, z))
                {
                    continue;
                }
                scene.Add(model, x, z, scene.Rng.Next(360), scene.Uniform(0.6f, 0.95f));
            }
        }
    }

    private static void Require(bool placed, string what)
    {
        if (!placed) throw new InvalidOperationException($"{Prefix}: {what} does not fit");
    }

    private static float BoxDistance(float x, float z, float minX, float maxX, float minZ, float maxZ)
    {
        float dx = MathF.Max(0f, MathF.Max(minX - x, x - maxX)), dz = MathF.Max(0f, MathF.Max(minZ - z, z - maxZ));
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
