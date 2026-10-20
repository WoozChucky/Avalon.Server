using static Avalon.ChunkGen.SceneryShapes;
using Outline = System.Collections.Generic.IReadOnlyList<(float X, float Z)>;

namespace Avalon.ChunkGen;

// The forest's mossy cliffs (ForestScenery's summary): plateau tiers inside the band, their faces dressed with layered
// face sections, ledges, roots and creepers, scree at their foot.
public static partial class ForestScenery
{
    /// <summary>
    /// The cliff face sections at scale 1: the length and height of each one's rock, how far its middle bulges out,
    /// how far its ends turn back and how far it leans back over its height. These are the shape each model was built
    /// to, which the kit's bounds table does not hold (a box also takes in the buried foot, the turf cap and the strata
    /// standing proud of the rock), so they must change when a model does.
    /// </summary>
    private static readonly (string Model, float Length, float Height, float Bulge, float Ends, float Lean)[] s_faces =
    [
        ("avalon_cliff_face_1", 7f, 6.4f, 0.18f, 0.6f, 0.75f),
        ("avalon_cliff_face_2", 6.2f, 7.2f, 0.25f, 0.65f, 0.6f),
        ("avalon_cliff_face_3", 7.8f, 7.8f, 0.15f, 0.55f, 0.9f),
    ];

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
                    if (best is not { } held || MathF.Abs(p.H - target) - 0.1f * p.Slope < MathF.Abs(held.H - target) - 0.1f * held.Slope)
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
}
