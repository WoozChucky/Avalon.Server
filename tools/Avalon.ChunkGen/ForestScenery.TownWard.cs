using Avalon.World.ChunkLayouts;
using static Avalon.ChunkGen.SceneryShapes;

namespace Avalon.ChunkGen;

// The forest's town-ward scene (ForestScenery's summary): the eight spots round the entry chunk, composed as one scene
// in world metres and cut into a piece per spot.
public static partial class ForestScenery
{
    private const string TownWardPrefix = "forest_townward";

    // World metres, +X east, +Z north. The entry chunk (forest_entry_01, rotation 0) holds cell (0, 0), its only exit
    // north; its portal back to the town stands at (15, 5), 5 m in from its closed south side, and its tree walls leave
    // the south corners, the west side's south third and the east side's ends open.

    /// <summary>
    /// The road from the town, up the middle of spot (0, -1): x 13 to 17, whole metres, so the ground's 1 m triangles
    /// give it straight edges; from 2 m in from the cell's south edge to the worn ground before the wall.
    /// </summary>
    private const float RoadX = 15f, RoadHalf = 2f, RoadFrom = -28f, RoadTo = -5f;

    /// <summary>The low field wall behind the portal: the line its sections' bodies are centred on, and its ends.</summary>
    private const float WallZ = -3.4f, WallFrom = 3f, WallTo = 27f;

    /// <summary>The road's grass verges, either side, and the grass headland round each field, in whole metres.</summary>
    private const float VergeWidth = 1f, HeadlandWidth = 2f;

    /// <summary>The worn ground where the road ends before the wall: its centre and half-sizes.</summary>
    private const float RoadEndZ = -5.6f, RoadEndHalfX = 4.2f, RoadEndHalfZ = 1.9f;

    /// <summary>The fields' tilled ground, whole metres; the west field's strip east of its stubble line is harvested.</summary>
    private static readonly Field s_eastField = new(37f, 59f, -29f, -4f, Stubble: null);

    private static readonly Field s_westField = new(-29f, -6f, -29f, -4f, Stubble: -13f);

    /// <summary>
    /// A field: its tilled ground (whole metres), standing wheat on it west of <paramref name="Stubble" /> (all of it
    /// when null), stubble east of it.
    /// </summary>
    private sealed record Field(float MinX, float MaxX, float MinZ, float MaxZ, float? Stubble)
    {
        public bool Holds(float x, float z, float margin = 0f) =>
            x > MinX - margin && x < MaxX + margin && z > MinZ - margin && z < MaxZ + margin;
    }

    /// <summary>The wheat's spacing (town-kit NOTES): stands 1 m apart along a row, rows 0.95 m apart.</summary>
    private const float WheatAlong = 1f, WheatRows = 0.95f;

    /// <summary>A fence section's length: sections stand this far apart, end to end.</summary>
    private const float FenceSection = 2f;

    /// <summary>The fences stand this far outside their field's tilled ground.</summary>
    private const float FenceOut = 0.6f;

    /// <summary>
    /// The eight town-ward pieces: the scene composed over spots (-1..1, -1..1) minus the entry's (0, 0), one dresser
    /// per spot (its name's random stream, its cell's origin), cut by SceneryComposer into <c>forest_townward_&lt;dx&gt;_&lt;dz&gt;</c>.
    /// The woods thin toward the south. Spot (0, -1), behind the portal, carries the road from the town to a low field
    /// wall with brambles along the cell's north band, the visible barrier behind the portal; spots (-1, -1) and
    /// (1, -1) the fields' edges, fences and wheat, scattered trees; the rest the woods' fringe, deadfall and thorn
    /// toward the entry's corners. Every piece meets its edges at y 0 on forest floor and carries its barrier band.
    /// </summary>
    private static IReadOnlyList<SceneryPiece> TownWard(IReadOnlyDictionary<string, ModelBounds> kit)
    {
        int seed = ForestDresser.StableSeed(TownWardPrefix);
        var terrain = new SceneryTerrain((x, z) => TownWardHeight(seed, x, z), (x, z) => TownWardMaterial(seed, x, z));
        List<(int X, int Z)> spots = [];
        List<DecorItem> decor = [];
        foreach (int dz in new[] { -1, 0, 1 })
        {
            foreach (int dx in new[] { -1, 0, 1 })
            {
                if ((dx, dz) == (0, 0)) continue;
                spots.Add((dx, dz));
                var d = new SceneryDresser(SceneryComposer.CellName(TownWardPrefix, dx, dz), terrain, kit) { Origin = (dx * Cell, dz * Cell) };
                switch ((dx, dz))
                {
                    case (0, -1):
                        RoadToTheWall(d);
                        break;
                    case (1, -1):
                        FieldEdge(d, s_eastField, fenceEast: false, EastFieldBoundary, scarecrow: (40.6f, -10.2f), lone: [(TreeSpecies.CommonTree, 48.5f, -18f, 1.25f)]);
                        break;
                    case (-1, -1):
                        FieldEdge(d, s_westField, fenceEast: true, WestFieldBoundary, scarecrow: null, lone: [(TreeSpecies.CommonTree, -21.5f, -19f, 1.3f)]);
                        break;
                    default:
                        Fringe(d, dx, dz);
                        break;
                }
                decor.AddRange(d.Items);
            }
        }

        return SceneryComposer.Cut(TownWardPrefix, spots, terrain, decor, DecorJsonWriter.ForestKit,
            (cell, piece) => piece with { Kind = SceneryKind.TownWard, Family = "townward", Spot = cell });
    }

    // ---- ground ----

    /// <summary>The cell-local position of a world point (0-30 on x and z).</summary>
    private static (float X, float Z) CellLocal(float x, float z) => (x - MathF.Floor(x / Cell) * Cell, z - MathF.Floor(z / Cell) * Cell);

    /// <summary>Metres from a world point to the road's centre line (the road's whole run, its end included).</summary>
    private static float RoadDistance(float x, float z) => SegmentDistance(x, z, (RoadX, RoadFrom), (RoadX, RoadTo));

    /// <summary>Whether a world point lies on the road: its strip, and the worn ground where it ends before the wall.</summary>
    private static bool OnRoad(int seed, float x, float z)
    {
        if (MathF.Abs(x - RoadX) < RoadHalf && z > RoadFrom && z < RoadTo) return true;
        float ex = (x - RoadX) / RoadEndHalfX, ez = (z - RoadEndZ) / RoadEndHalfZ;
        return ex * ex + ez * ez < 0.75f + 0.5f * Noise(seed + 3, x, z, 2.5f);
    }

    /// <summary>
    /// The scene's ground: the old woods' gentle relief, laid flat under the fields, the road and the wall, the road
    /// worn a little below its verges; everything fading to exactly 0 at every cell edge.
    /// </summary>
    private static float TownWardHeight(int seed, float x, float z)
    {
        (float lx, float lz) = CellLocal(x, z);
        float edge = EdgeDistance(lx, lz);
        float noise = (Noise(seed, x, z, 9f) - 0.5f) * 0.7f + (Noise(seed + 1, x, z, 3.5f) - 0.5f) * 0.2f;
        float flat = MathF.Min(Smooth(1.5f, 4f, RoadDistance(x, z) - RoadHalf), Smooth(1.2f, 3.5f, MathF.Abs(z - WallZ) + MathF.Max(0f, MathF.Max(WallFrom - x, x - WallTo))));
        flat = MathF.Min(flat, Smooth(0f, 3f, FieldDistance(x, z)));
        float worn = -0.06f * (1f - Smooth(RoadHalf - 0.8f, RoadHalf + 0.3f, RoadDistance(x, z)));
        return noise * flat * Smooth(0.5f, 6f, edge) + worn * Smooth(0f, 1.5f, edge);
    }

    /// <summary>Metres from a world point to the nearer field's tilled ground, 0 on it.</summary>
    private static float FieldDistance(float x, float z) =>
        MathF.Min(BoxDistance(x, z, s_eastField), BoxDistance(x, z, s_westField));

    private static float BoxDistance(float x, float z, Field f)
    {
        float dx = MathF.Max(0f, MathF.Max(f.MinX - x, x - f.MaxX)), dz = MathF.Max(0f, MathF.Max(f.MinZ - z, z - f.MaxZ));
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// The scene's ground materials: forest floor along every cell edge (the outer metre), tilled soil under the fields,
    /// dirt on the road and where it ends, meadow (rough grass) on the road's verges and the fields' headlands; forest
    /// floor elsewhere. The meadow's edges run on whole metres, along the ground's 1 m triangles: a light ground's
    /// saw-tooth edge beside the dark floor reads as drawn.
    /// </summary>
    private static string TownWardMaterial(int seed, float x, float z)
    {
        (float lx, float lz) = CellLocal(x, z);
        if (EdgeDistance(lx, lz) < 1f) return Floor;
        if (s_eastField.Holds(x, z) || s_westField.Holds(x, z)) return "tilled_soil";
        if (OnRoad(seed, x, z)) return "dirt_road";
        bool verge = MathF.Abs(x - RoadX) < RoadHalf + VergeWidth && z > RoadFrom && z < RoadTo - 1f;
        bool headland = s_eastField.Holds(x, z, HeadlandWidth) || s_westField.Holds(x, z, HeadlandWidth);
        return verge || headland ? "meadow" : Floor;
    }

    // ---- spot (0, -1): the road to the wall ----

    /// <summary>
    /// The road from the town coming north up the cell to a low wall of field-wall sections across its north band,
    /// brambles grown along and over it and a log fallen before it where the road ends: the visible barrier behind the
    /// portal. A grass strip down the road's middle and tufts along its verges, thin woods on either side, a few
    /// scattered trees and a boulder by the road's end.
    /// </summary>
    private static void RoadToTheWall(SceneryDresser d)
    {
        int seed = ForestDresser.StableSeed(TownWardPrefix);

        // The wall: sections laid end to end along x, their bodies on WallZ; the tumbled sections' fallen stones lie
        // toward the road.
        foreach ((string model, float x, int yaw) in new[]
        {
            ("avalon_field_wall_1", 5f, 0), ("avalon_field_wall_2", 10f, 180), ("avalon_field_wall_1", 15f, 180),
            ("avalon_field_wall_2", 20f, 180), ("avalon_field_wall_1", 25f, 0),
        })
        {
            if (!d.TryAdd(model, x, WallZ, yaw, 1f))
                throw new InvalidOperationException($"{d.Name}: the wall's {model} at ({ForestText.N(x)}, {ForestText.N(WallZ)}) does not fit");
            for (float t = -2.5f; t <= 2.5f; t += 1f) d.Take(x + t, WallZ, 0.55f);
        }

        // Where the road ends: a bramble grown over the wall, another at its foot, a log fallen across the way.
        foreach ((string model, float x, float z, int yaw, float scale) in new[]
        {
            ("avalon_bramble_2", 15.6f, WallZ + 0.35f, 40, 1.2f), ("avalon_bramble_1", 13.3f, WallZ - 0.8f, 200, 0.95f),
        })
        {
            if (!d.TryAdd(model, x, z, yaw, scale))
                throw new InvalidOperationException($"{d.Name}: the {model} over the road's end at ({ForestText.N(x)}, {ForestText.N(z)}) does not fit");
            d.Take(x, z, d.Spacing(model, scale, 0.35f));
        }
        Log(d, "avalon_log_fallen_2", 16.4f, -5.3f, 172, 0.95f);

        // Brambles along the wall's north side, their tops over it, thicker toward its ends; thorn closing both ends.
        for (float x = WallFrom + 0.6f; x < WallTo - 0.4f; x += d.Uniform(1.9f, 2.6f))
        {
            string model = d.Rng.Next(3) == 0 ? "avalon_bramble_1" : "avalon_bramble_2";
            for (float scale = d.Uniform(0.9f, 1.1f); scale > 0.75f; scale -= 0.08f)
            {
                if (d.TryPlace(model, x, d.Uniform(-2.3f, -1.75f), 0.6f * scale, scale)) break;
            }
        }
        Cluster(d, 1.6f, -3.2f, ["avalon_bramble_2", "avalon_hedge_dark", "avalon_bramble_1"], 1.2f, 0.9f, 1.1f);
        Cluster(d, 28.4f, -3.4f, ["avalon_bramble_2", "avalon_bramble_1", "avalon_hedge_dark"], 1.2f, 0.9f, 1.1f);
        // Clumps at the wall's south foot, the stone showing between them.
        foreach (float x in new[] { 4.2f, 8.6f, 21.6f, 25.8f })
            Cluster(d, x, WallZ - 1.1f, ["avalon_bramble_1", "Fern_1", "Grass_Wispy_Tall"], 0.9f, 0.8f, 1.05f, keep: 0.4f);

        // The road: a strip of grass down its middle between the wheel ruts, tufts breaking its verges, a few pebbles.
        bool Road(float x, float z) => OnRoad(seed, x, z);
        for (float z = RoadFrom + 1.2f; z < RoadTo - 1f; z += d.Uniform(0.65f, 1.05f))
        {
            if (d.Rng.NextDouble() < 0.2) continue;
            d.TryAdd(d.Pick(["Grass_Wispy_Short", "Grass_Common_Short", "Grass_Wispy_Short", "Clover_1"]), RoadX + d.Uniform(-0.15f, 0.15f), z,
                d.Rng.Next(360), d.Uniform(0.5f, 0.72f));
        }
        (string Model, int Weight)[] verge =
        [
            ("Grass_Common_Short", 6), ("Grass_Wispy_Short", 5), ("Grass_Wispy_Tall", 3), ("Grass_Common_Tall", 2), ("Clover_2", 2), ("Plant_7", 2),
            ("Flower_4_Single", 1), ("Flower_3_Single", 1),
        ];
        foreach (float side in new[] { -1f, 1f })
        {
            for (float z = RoadFrom + 0.5f; z < RoadTo - 0.5f; z += d.Uniform(0.8f, 1.5f))
            {
                float x = RoadX + side * (RoadHalf + d.Uniform(-0.35f, 0.6f));
                string model = d.PickWeighted(verge);
                d.TryPlace(model, x, z, d.Spacing(model, 0.8f, 0.2f), d.Uniform(0.7f, 1.05f));
            }
        }
        for (int i = 0; i < 14; i++)
        {
            float x = RoadX + d.Uniform(-RoadHalf + 0.3f, RoadHalf - 0.3f), z = d.Uniform(RoadFrom + 0.5f, RoadTo);
            d.TryPlace(d.Pick(s_scree), x, z, 0.2f, d.Uniform(1.1f, 1.9f), 0.02f);
        }

        // A mossy boulder by the road's end, as the spike had one by the portal; scattered trees, the woods thin here.
        if (!d.TryPlace("avalon_boulder_2", 20.6f, -8.4f, 1.2f, 0.95f, 0.18f, yaw: 115))
            throw new InvalidOperationException($"{d.Name}: the boulder by the road does not fit");
        Cluster(d, 21.4f, -9.6f, ["avalon_rock_mossy_1", "avalon_rock_mossy_3"], 1f, 0.6f, 0.9f);
        d.TryTree(TreeSpecies.CommonTree, 7f, -15.5f, 1.15f);
        d.TryTree(TreeSpecies.Pine, 23.5f, -21.5f, 1.05f);
        d.TryTree(TreeSpecies.DeadTree, 24.8f, -13.2f, 0.9f);
        d.TryTree(TreeSpecies.CommonTree, 8.6f, -24.2f, 1f);

        // The band, its trees in one row and kept off the road, its sides and the wall; undergrowth off the road and
        // the wall's line (on the south band it flanks the road's first metres).
        bool NearWall(float x, float z) => z > WallZ - 1f && z < WallZ + 0.9f && x > WallFrom - 0.5f && x < WallTo + 0.5f;
        bool Verge(float x, float z) => MathF.Abs(x - RoadX) < RoadHalf + 1.2f && z < RoadTo + 0.5f;
        d.BarrierBand(TreeMix.Broadleaf, s_sides, (x, z) => Road(x, z) || NearWall(x, z),
            (x, z) => MathF.Abs(x - RoadX) < 6.5f || (z > -8f && x > 1.5f && x < 28.5f), treeRows: 1, undergrowthPer100: 16f);

        // Bushes and flowers where the woods open toward the town, the darker understorey under the woods' edge.
        bool Open(float x, float z) => z < -14f && !Road(x, z) && !Verge(x, z);
        d.Scatter([("Bush_Common_Flowers", 2), ("Flower_3_Group", 2), ("Flower_4_Group", 1), ("Grass_Common_Tall", 3), ("Plant_1_Big", 1), ("Fern_1", 2)], 16,
            0.8f, 1.15f, (x, z) => Open(x, z) && EdgeDistance(x - d.Origin.X, z - d.Origin.Z) > 3f);
        d.Understorey(18, (x, z) => !Open(x, z) && !Road(x, z) && !Verge(x, z) && !NearWall(x, z) && z < WallZ - 1.4f && EdgeDistance(x - d.Origin.X, z - d.Origin.Z) > 3f);
        d.MossyRocks(2, (x, z) => !Road(x, z) && !Verge(x, z) && EdgeDistance(x - d.Origin.X, z - d.Origin.Z) > 6f);
        d.GroundCover(70, (x, z) => !Road(x, z) && !NearWall(x, z));
    }

    // ---- spots (-1, -1) and (1, -1): the fields' edges ----

    /// <summary>The east field's boundary with the road's cell: a hedgerow along the cell's west band, a tree or two in it.</summary>
    private static void EastFieldBoundary(SceneryDresser d)
    {
        // The trees first, so the hedge grows round them.
        foreach ((float x, float z, float scale) in new[] { (4f, 9.5f, 1.1f), (4f, 19f, 1.05f), (4f, 25.6f, 0.95f) })
        {
            bool planted = false;
            for (int attempt = 0; !planted && attempt < 8; attempt++)
                planted = d.TryTree(TreeSpecies.CommonTree, d.Origin.X + x, d.Origin.Z + z, scale);
            if (!planted)
                throw new InvalidOperationException($"{d.Name}: the hedgerow's tree at ({ForestText.N(d.Origin.X + x)}, {ForestText.N(d.Origin.Z + z)}) does not fit");
        }
        (string Model, int Weight)[] hedge = [("avalon_hedge_dark", 3), ("avalon_hedge_green", 2), ("Bush_Common_Flowers", 2), ("avalon_bramble_1", 1)];
        for (float z = d.Origin.Z + 1.4f; z < d.Origin.Z + Cell - 1.4f; z += d.Uniform(1.5f, 1.9f))
        {
            string model = d.PickWeighted(hedge);
            for (float scale = d.Uniform(0.95f, 1.25f); scale > 0.75f; scale -= 0.1f)
            {
                if (d.TryPlace(model, d.Origin.X + 1.5f + d.Uniform(-0.2f, 0.2f), z, 0.5f * scale, scale)) break;
            }
        }
    }

    /// <summary>The west field's boundary with the road's cell: an old field wall along the cell's east band, brambles at its ends.</summary>
    private static void WestFieldBoundary(SceneryDresser d)
    {
        // Along z (a quarter turn), the tumbled sections' stones toward the road.
        float x = d.Origin.X + 27.4f;
        foreach ((string model, float z) in new[]
        {
            ("avalon_field_wall_1", -25f), ("avalon_field_wall_2", -20f), ("avalon_field_wall_1", -15f), ("avalon_field_wall_2", -10f), ("avalon_field_wall_1", -5f),
        })
        {
            if (!d.TryAdd(model, x, z, 90, 1f))
                throw new InvalidOperationException($"{d.Name}: the field wall's {model} at ({ForestText.N(x)}, {ForestText.N(z)}) does not fit");
            for (float t = -2.5f; t <= 2.5f; t += 1f) d.Take(x, z + t, 0.55f);
        }
        Cluster(d, x - 0.2f, -1.6f, ["avalon_bramble_2", "avalon_bramble_1"], 0.6f, 0.85f, 1f);
        Cluster(d, x - 0.4f, -28.3f, ["avalon_bramble_1", "avalon_hedge_dark"], 0.6f, 0.85f, 1f);
        foreach (float z in new[] { -22.5f, -12.6f, -7.4f })
            Cluster(d, x - 1f, z, ["avalon_bramble_1", "Grass_Wispy_Tall", "Fern_1"], 0.8f, 0.8f, 1f, keep: 0.4f);
    }

    /// <summary>
    /// A field's edge: wheat standing in rows on the tilled ground (stubble on a harvested strip, with haystacks and
    /// bales), a wooden fence along its north side (and its east side when <paramref name="fenceEast" />, its west side
    /// otherwise), the boundary with the road's cell, a scarecrow, a lone tree or two in the field; outside the fence a
    /// meadow margin of tall grass and flowers, the woods' thin fringe beyond, the band round it all.
    /// </summary>
    private static void FieldEdge(
        SceneryDresser d, Field field, bool fenceEast, Action<SceneryDresser> boundary, (float X, float Z)? scarecrow,
        (TreeSpecies Species, float X, float Z, float Scale)[] lone)
    {
        int seed = ForestDresser.StableSeed(TownWardPrefix);
        foreach ((TreeSpecies species, float x, float z, float scale) in lone)
        {
            if (!d.TryTree(species, x, z, scale))
                throw new InvalidOperationException($"{d.Name}: the lone tree at ({ForestText.N(x)}, {ForestText.N(z)}) does not fit");
        }
        if (scarecrow is { } s)
        {
            if (!d.TryPlace("avalon_scarecrow_1", s.X, s.Z, 0.9f, 1f, 0.05f, yaw: 225))
                throw new InvalidOperationException($"{d.Name}: the scarecrow does not fit");
        }
        // No wheat round the lone trees' trunks and the scarecrow.
        bool Kept(float x, float z) =>
            lone.Any(t => SceneryDresser.Distance(x, z, t.X, t.Z) < 2.2f) || scarecrow is { } c && SceneryDresser.Distance(x, z, c.X, c.Z) < 0.9f;

        // The harvested strip (the west field's): haystacks and bales, stubble rows.
        bool Harvested(float x) => field.Stubble is { } line && x >= line;
        if (field.Stubble is not null)
        {
            foreach ((string model, float x, float z, int yaw, float scale) in new[]
            {
                ("avalon_haystack_1", -9.2f, -20.6f, 20, 1f), ("avalon_haystack_2", -10.4f, -13.9f, 140, 0.92f),
                ("avalon_hay_bale_1", -7.9f, -9.2f, 70, 1f), ("avalon_hay_bale_2", -12f, -17.4f, 15, 1f), ("avalon_hay_bale_1", -7.6f, -25.2f, 200, 0.95f),
                ("avalon_hay_bale_2", -8.6f, -5.6f, 110, 0.95f),
            })
            {
                if (!d.TryPlace(model, x, z, d.Spacing(model, scale, 0.5f), scale, 0.02f, yaw))
                    throw new InvalidOperationException($"{d.Name}: the {model} at ({ForestText.N(x)}, {ForestText.N(z)}) does not fit");
            }
        }

        // The wheat (and the stubble, stands cut short), in rows along x.
        for (float z = field.MinZ + 0.5f; z <= field.MaxZ - 0.45f; z += WheatRows)
        {
            for (float x = field.MinX + 0.5f; x <= field.MaxX - 0.5f; x += WheatAlong)
            {
                float jx = x + d.Uniform(-0.12f, 0.12f), jz = z + d.Uniform(-0.12f, 0.12f);
                int yaw = d.Rng.Next(360);
                if (Harvested(x))
                {
                    if (d.Open(jx, jz, 0.3f) && d.Rng.NextDouble() < 0.7)
                        d.TryAdd(d.Pick(["avalon_wheat_1", "avalon_wheat_2"]), jx, jz, yaw, d.Uniform(0.26f, 0.34f));
                    continue;
                }
                if (Kept(jx, jz)) continue;
                string model = d.PickWeighted([("avalon_wheat_1", 1), ("avalon_wheat_2", 2), ("avalon_wheat_3", 1)]);
                d.TryAdd(model, jx, jz, yaw, d.Uniform(0.95f, 1.06f));
            }
        }

        // The fences from the field's corner by the road: one along its north side, one down its side toward the road.
        float fenceZ = field.MaxZ + FenceOut, sideX = fenceEast ? field.MaxX + FenceOut : field.MinX - FenceOut;
        float farX = fenceEast ? field.MinX - FenceOut : field.MaxX + FenceOut;
        FenceRun(d, sideX, fenceZ, fenceEast ? -1f : 1f, 0f, (int)MathF.Floor(MathF.Abs(farX - sideX) / FenceSection));
        FenceRun(d, sideX, fenceZ, 0f, -1f, (int)MathF.Floor((fenceZ - (field.MinZ - FenceOut)) / FenceSection));
        bool OnFence(float x, float z, float margin) =>
            (MathF.Abs(z - fenceZ) < margin && x > field.MinX - FenceOut - margin && x < field.MaxX + FenceOut + margin)
            || (MathF.Abs(x - sideX) < margin && z > field.MinZ - FenceOut - margin && z < fenceZ + margin);

        boundary(d);

        // The margin outside the fence: tall grass and flowers along its foot.
        d.Scatter([("Grass_Common_Tall", 4), ("Grass_Wispy_Tall", 4), ("Flower_3_Single", 1), ("Flower_4_Single", 1), ("Plant_1", 2), ("Clover_2", 2), ("Fern_1", 1)], 40,
            0.75f, 1.1f, (x, z) => !field.Holds(x, z, 0.3f) && OnFence(x, z, 1.3f) && !OnFence(x, z, 0.35f), spread: 0.2f);

        // The band, one row of trees kept off the field and its fence; undergrowth off the field.
        d.BarrierBand(TreeMix.Broadleaf, s_sides, (x, z) => field.Holds(x, z, 0.4f) || OnFence(x, z, 0.5f),
            (x, z) => field.Holds(x, z, 2.5f) || OnFence(x, z, 2f) || InView(x, z, (px, pz) => OnFence(px, pz, 0.5f)), treeRows: 1, undergrowthPer100: 14f);

        bool Meadow(float x, float z) => TownWardMaterial(seed, x, z) == "meadow";
        d.Scatter([("Bush_Common_Flowers", 2), ("Flower_3_Group", 2), ("Flower_4_Group", 1), ("Grass_Common_Tall", 3), ("Fern_1", 2)], 10,
            0.8f, 1.1f, (x, z) => Meadow(x, z) && !OnFence(x, z, 1f) && EdgeDistance(x - d.Origin.X, z - d.Origin.Z) > 3f);
        d.Understorey(12, (x, z) => !Meadow(x, z) && !field.Holds(x, z, 1f) && !OnFence(x, z, 1f) && EdgeDistance(x - d.Origin.X, z - d.Origin.Z) > 3f);
        d.GroundCover(40, (x, z) => !field.Holds(x, z, 0.3f) && !OnFence(x, z, 0.4f));
    }

    /// <summary>
    /// A run of <paramref name="sections" /> wooden fence sections end to end from (x, z) along the direction (one of
    /// x or z, plus or minus): a single section first, then the two extensions in turn, each standing on the ground.
    /// </summary>
    private static void FenceRun(SceneryDresser d, float x, float z, float dirX, float dirZ, int sections)
    {
        for (int i = 0; i < sections; i++)
        {
            float at = FenceSection / 2f + FenceSection * i;
            (float fx, float fz) = (x + dirX * at, z + dirZ * at);
            string model = i == 0 ? "Prop_WoodenFence_Single" : i % 2 == 1 ? "Prop_WoodenFence_Extension1" : "Prop_WoodenFence_Extension2";
            if (!d.TryAdd(model, fx, fz, dirX == 0f ? 90 : 0, 1f))
                throw new InvalidOperationException($"{d.Name}: the fence's {model} at ({ForestText.N(fx)}, {ForestText.N(fz)}) does not fit");
            d.Take(fx, fz, 0.5f);
        }
    }

    // ---- spots (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1): the woods' fringe ----

    /// <summary>
    /// The woods beside and beyond the entry: deadfall (fallen logs crossed, brambles over them) and thorn
    /// toward the entry's corners, where its tree walls leave gaps; the band; a few trees and the floor. The north spots
    /// are older woods, an ancient tree among them.
    /// </summary>
    private static void Fringe(SceneryDresser d, int dx, int dz)
    {
        float ox = d.Origin.X, oz = d.Origin.Z;
        List<(float X, float Z)> held = [];
        // The entry's corners and open stretches facing this spot, as (x, z) in world metres: deadfall, then thorn.
        (float X, float Z, bool Deadfall)[] spots = (dx, dz) switch
        {
            (-1, 0) => [(-4f, 5.5f, true), (-2.8f, 26.8f, false)],
            (1, 0) => [(33.8f, 3.2f, true), (33.2f, 26.2f, true)],
            (-1, 1) => [(-3.4f, 33.6f, true), (-8f, 34.8f, false)],
            (1, 1) => [(33.4f, 33.8f, true), (38.2f, 33.2f, false)],
            _ => [],
        };
        foreach ((float x, float z, bool deadfall) in spots)
        {
            if (deadfall) Deadfall(d, x, z);
            else Thorn(d, x, z);
            held.Add((x, z));
        }

        if (dz == 1)
        {
            (int variant, float ax, float az) = (dx, dz) switch
            {
                (-1, 1) => (2, ox + 13.5f, oz + 15.5f),
                (1, 1) => (4, ox + 16f, oz + 14.5f),
                _ => (1, ox + 15f, oz + 16f),
            };
            if (!d.TryAncient(variant, ax, az, d.Rng.Next(360)))
                throw new InvalidOperationException($"{d.Name}: the ancient tree does not fit");
            held.Add((ax, az));
        }

        bool Feature(float x, float z, float reach) => held.Any(h => SceneryDresser.Distance(x, z, h.X, h.Z) < reach);
        TreeMix mix = dz == 0 ? TreeMix.Broadleaf : TreeMix.Mixed;
        d.BarrierBand(mix, s_sides, (x, z) => Feature(x, z, 1.6f), (x, z) => Feature(x, z, 3.6f) || InView(x, z, (px, pz) => Feature(px, pz, 2f)),
            treeRows: dz == 0 ? 1 : 2, undergrowthPer100: dz == 0 ? 18f : SceneryDresser.BandUndergrowthPer100);
        Inner(d, mix, saplings: dz == 0 ? 3 : 2, understorey: dz == 0 ? 45 : 60, cover: 55, rocks: 3, clear: (x, z) => Feature(x, z, 1.2f),
            treesClear: (x, z) => InView(x, z, (px, pz) => Feature(px, pz, 2f)));
    }

    /// <summary>Deadfall: two fallen logs crossed, mushrooms on them, brambles grown over them.</summary>
    private static void Deadfall(SceneryDresser d, float x, float z)
    {
        int yaw = d.Rng.Next(60, 120);
        Log(d, "avalon_log_fallen_1", x, z, yaw, 1.05f);
        Log(d, "avalon_log_fallen_2", x + d.Uniform(-0.8f, 0.8f), z + d.Uniform(-0.8f, 0.8f), yaw + d.Rng.Next(50, 80), 0.95f);
        Cluster(d, x, z, ["avalon_bramble_2", "avalon_bramble_1", "Fern_1"], 1.8f, 0.85f, 1.1f, keep: 0.4f);
    }

    /// <summary>Thorn: a mass of brambles and a dark hedge.</summary>
    private static void Thorn(SceneryDresser d, float x, float z) =>
        Cluster(d, x, z, ["avalon_bramble_2", "avalon_bramble_1", "avalon_bramble_2", "avalon_hedge_dark"], 1.6f, 0.9f, 1.15f, keep: 0.4f);
}
