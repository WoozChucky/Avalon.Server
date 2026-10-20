using Avalon.World.ChunkLayouts;
using static Avalon.ChunkGen.SceneryShapes;
using Outline = System.Collections.Generic.IReadOnlyList<(float X, float Z)>;

namespace Avalon.ChunkGen;

// The forest's exit caps (ForestScenery's summary): every open exit's path ends under an obstacle across its edge band.
public static partial class ForestScenery
{
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
        // The lip: the brink's wall line, 2 m in at the edge's middle (s_brinkRavine, s_capGorge).
        Obstacle.Brink => (1, 2f),
        // The giant's near flank (TrunkU, TrunkV, TrunkYaw): its middle crosses the path 5.1 m in, its flank 3.7 m in.
        Obstacle.Trunk => (3, 3.4f),
        // The arch over the path's end (Thicket): its brambles stand 4.5 m and more in, their leaves hanging toward 3 m.
        Obstacle.Thicket => (3, 4.2f),
        // The rockfall's front stones (s_rockfall): the middle one's box reaches back to 2.5 m, a fallen one beside the
        // path stands at 2.6 m.
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
            else if (roll < 0.58)
            {
                // Kit rocks only as small scree: larger, their pale faces read as dice beside the mossy boulders.
                float scale = d.Uniform(0.16f, KitScreeMaxScale);
                d.TryPlace(d.Pick(["Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3"]), x, z, 1.4f * scale, scale, 0.08f);
            }
            else
            {
                float scale = d.Uniform(0.35f, 0.85f);
                d.TryPlace(d.Pick(s_mossyRocks), x, z, 0.9f * scale, scale, 0.05f);
            }
        }

        // The rest of the edge, blocked by fallen stones and thorn in four rows to 8 m in: boulders and mossy rocks
        // mostly, brambles and dark hedges between them, the odd fern.
        (string Model, int Weight)[] flank =
        [
            ("avalon_boulder_1", 3), ("avalon_boulder_2", 3), ("avalon_boulder_3", 1), ("avalon_rock_mossy_1", 2), ("avalon_rock_mossy_2", 2),
            ("avalon_rock_mossy_3", 2), ("avalon_bramble_1", 3), ("avalon_bramble_2", 2), ("avalon_hedge_dark", 1), ("Fern_1", 1), ("Plant_1_Big", 1),
        ];
        for (int row = 0; row < 4; row++)
        {
            for (float u = 0.8f + row * 0.7f; u < 29.2f; u += d.Uniform(1.1f, 1.7f))
            {
                if (u is > 9.2f and < 20.8f) continue;
                (float x, float z) = e.At(u, 0.9f + row * 1.8f + d.Uniform(0f, 1.6f));
                if (!free(x, z)) continue;
                string model = d.PickWeighted(flank);
                bool rock = model.StartsWith("avalon_boulder_", StringComparison.Ordinal) || model.StartsWith("avalon_rock_", StringComparison.Ordinal);
                float scale = model switch
                {
                    "avalon_boulder_3" => d.Uniform(0.8f, 0.95f),
                    _ when model.StartsWith("avalon_boulder_", StringComparison.Ordinal) => d.Uniform(0.85f, 1.15f),
                    _ when rock => d.Uniform(0.7f, 1.1f),
                    _ => d.Uniform(1f, 1.35f),
                };
                d.TryPlace(model, x, z, d.Spacing(model, scale, 0.3f), scale, rock ? 0.18f * scale : 0f, yaw: e.Turn(d.Rng.Next(360)));
            }
        }

        bool Heap(float x, float z)
        {
            (float u, float v) = e.Of(x, z);
            return u is > 6.5f and < 24f && v is > 1f and < 12.5f;
        }
        d.Scatter([("Fern_1", 5), ("Bush_Common_Flowers", 2), ("Plant_1_Big", 2), ("Grass_Wispy_Tall", 3), ("avalon_hedge_dark", 1)], 24, 0.8f, 1.2f,
            (x, z) => Heap(x, z) && free(x, z));
        // The heap and the stones and thorn closing the edge: the floor's ferns keep off them, and so do the band's trees
        // (Cap passes this to BarrierBand's keepTreesClear too). Within 8.4 m of the capped edge that is the whole edge,
        // its corners included, so the bands beside it carry no tree for their first 8.4 m either: the flank's four rows
        // of stones and thorn run into both corners and stand there instead.
        return (x, z) => Heap(x, z) || e.Of(x, z).V < 8.4f;
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
            // A kit rock only as small as the rockfall's scree: larger, its pale faces read as dice.
            float scale = i < 5 ? d.Uniform(1.4f, 2.6f) : model == "Rock_Medium_2" ? d.Uniform(0.16f, KitScreeMaxScale) : d.Uniform(0.45f, 0.65f);
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
        // The arch over the path's end, placed whatever stands near (only the cell's edge refuses one): without it the
        // thicket would not end the path.
        foreach ((string model, float u, float v, int yaw, float scale) in new[]
        {
            ("avalon_bramble_2", 15.1f, 5.3f, 30, 1.3f), ("avalon_bramble_1", 13.6f, 6.6f, 200, 1.2f), ("avalon_hedge_dark", 16.9f, 6.3f, 0, 1.3f),
            ("avalon_bramble_1", 16.6f, 4.5f, 110, 1.05f), ("avalon_bramble_2", 12.7f, 4.6f, 300, 1.1f),
        })
        {
            (float x, float z) = e.At(u, v);
            if (!d.TryAdd(model, x, z, e.Turn(yaw), scale))
                throw new InvalidOperationException($"{d.Name}: the thicket's {model} over the path's end crosses the cell's edge at ({ForestText.N(x)}, {ForestText.N(z)})");
            d.Take(x, z, d.Spacing(model, scale, 0.25f));
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
            if (!d.TryAddAt(model, x, d.Height(x, z) - 0.02f, z, e.Turn(SceneryDresser.YawToward(-tv, tu) + d.Rng.Next(-4, 5)), 1f))
                throw new InvalidOperationException($"{d.Name}: the brink's {model} at ({ForestText.N(x)}, {ForestText.N(z)}) crosses the cell's edge");
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
}
