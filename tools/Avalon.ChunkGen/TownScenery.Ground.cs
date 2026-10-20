using static Avalon.ChunkGen.SceneryShapes;
using Polygon = System.Collections.Generic.IReadOnlyList<(float X, float Y, float Z)>;

namespace Avalon.ChunkGen;

// The town ring's ground (TownScenery's summary): its height and materials, the stream, the dirt drawn over it, and
// the measures the dressing keeps off.
public static partial class TownScenery
{
    // ---- the ground ----

    /// <summary>A house's garden between its back and the wall, and where its bed, shed, line, pile and compost stand.</summary>
    private sealed record Garden(
        int Index, bool West, float MinX, float MaxX, float MinZ, float MaxZ, (float MinX, float MaxX, float MinZ, float MaxZ) Bed,
        (float X, float Z, int Yaw) Shed, (float X, float Z, int Yaw) Line, (float X, float Z) Pile, (float X, float Z) Compost);

    /// <summary>
    /// The scene's ground: its height and material at any world point, the stream's water and the measures the dressing
    /// keeps off (roads, fields, gardens, buildings, the stream, the forest).
    /// </summary>
    private sealed class Ground
    {
        /// <summary>The gardens: west of the town along x, south of it along z (whole metres for the beds).</summary>
        public static readonly Garden[] Gardens =
        [
            new(1, false, 3.4f, 15.2f, -10.6f, -1.2f, (5f, 11f, -9f, -5f), (13.4f, -7.6f, 180), (9f, -3f, 0), (4.4f, -2.6f), (13.4f, -2.6f)),
            new(2, false, 17.6f, 27.6f, -10.6f, -1.2f, (19f, 25f, -9f, -5f), (25.6f, -8.2f, 180), (22f, -3.2f, 0), (19.6f, -2.4f), (26.2f, -3f)),
            new(3, false, 32.4f, 44.2f, -10.6f, -1.2f, (34f, 40f, -9f, -5f), (42.4f, -7.8f, 180), (37.6f, -2.8f, 0), (33.6f, -2.6f), (42.8f, -2.6f)),
            new(4, false, 46.4f, 57.6f, -10.6f, -1.2f, (48f, 54f, -9f, -5f), (55.6f, -8f, 180), (51.4f, -3f, 0), (47.6f, -2.4f), (56.2f, -2.8f)),
            new(5, true, -10.6f, -1.2f, 3.4f, 14.6f, (-9f, -5f, 5f, 11f), (-8f, 12.8f, 90), (-3f, 8.6f, 90), (-2.6f, 4.4f), (-2.6f, 13.4f)),
            new(6, true, -10.6f, -1.2f, 17.4f, 27.6f, (-9f, -5f, 19f, 25f), (-8.2f, 25.8f, 90), (-3.2f, 22.4f, 90), (-2.4f, 18.6f), (-3f, 26.2f)),
            new(7, true, -10.6f, -1.2f, 32.4f, 43.6f, (-9f, -5f, 34f, 40f), (-7.8f, 42f, 90), (-2.8f, 37.6f, 90), (-2.6f, 33.6f), (-2.6f, 42.6f)),
            new(8, true, -10.6f, -1.2f, 46.4f, 57.4f, (-9f, -5f, 48f, 54f), (-8f, 55.6f, 90), (-3f, 51.6f, 90), (-2.4f, 47.6f), (-2.8f, 56.2f)),
        ];

        private readonly IReadOnlyList<PlacedBuilding> _buildings;
        private readonly float[] _level;
        private readonly (float X, float Z)[][] _roads;
        private readonly float[] _roadHalf;

        public Ground(IReadOnlyList<PlacedBuilding> buildings)
        {
            _buildings = buildings;
            Course = Spline(s_stream, 1f);
            _roads = [Spline(s_road, 1f), Spline(s_laneSouth, 1f), [(14f, LaneZ), (BridgeX - 6f, LaneZ)], Spline(s_millTrack, 1f), [.. s_millYard]];
            _roadHalf = [2f, 1.5f, 1.5f, 1.1f, 1.3f];

            // The water's level along the course: under the land there, never rising, dropping at least WaterSlope a metre.
            _level = new float[Course.Count];
            for (int i = 0; i < Course.Count; i++)
            {
                (float x, float z) = Course[i];
                float under = Base(x, z) - WaterBelow;
                if (TownDistance(x, z) < 16f) under = MathF.Min(under, DellWater);
                _level[i] = i == 0 ? under : MathF.Min(under, _level[i - 1] - WaterSlope * SceneryDresser.Distance(Course[i - 1].X, Course[i - 1].Z, x, z));
            }
            BridgeLevel = WaterLevel(BridgeX, LaneZ) + 0.3f;
            Water = ScenerySurface.Strip([.. Course.Select((p, i) => (p.X, _level[i], p.Z))], StreamHalf, "water");

            List<Polygon> dirt = [];
            for (int r = 0; r < _roads.Length; r++)
                Band(dirt, _roads[r], -_roadHalf[r], _roadHalf[r], 4);
            Band(dirt, Course, StreamHalf - 0.15f, StreamHalf + BankEarth, 2);
            Band(dirt, Course, -StreamHalf - BankEarth, 0.15f - StreamHalf, 2);
            Overlay = ScenerySurface.Of(Dirt, dirt);
        }

        /// <summary>The stream's course, about 1 m between points, upstream first.</summary>
        public IReadOnlyList<(float X, float Z)> Course { get; }

        /// <summary>The stream's water surface.</summary>
        public ScenerySurface Water { get; }

        /// <summary>
        /// The dirt drawn over the ground along the roads and the lanes and on the stream's banks: strips of triangles
        /// <see cref="DirtLift" /> over it, so a winding road keeps smooth edges where the ground's own material changes
        /// only from one 1 m triangle to the next and would give it saw-teeth.
        /// </summary>
        public ScenerySurface Overlay { get; }

        /// <summary>The bridge's ends: the lane's level where it crosses (the arch springing 0.9 m lower, at the bed).</summary>
        public float BridgeLevel { get; }

        // ---- measures ----

        /// <summary>Metres from a point to the town's square, 0 inside it.</summary>
        public float TownDistance(float x, float z) => BoxDistance(x, z, 0f, TownMax, 0f, TownMax);

        /// <summary>Metres from a point to the stream's middle, and the course's segment and its fraction there.</summary>
        public float StreamDistance(float x, float z) => Nearest(x, z).Distance;

        public float WaterLevel(float x, float z)
        {
            (_, int i, float t) = Nearest(x, z);
            return _level[i] + (_level[i + 1] - _level[i]) * t;
        }

        /// <summary>Where the dark forest begins north of the fields, at a given x.</summary>
        public float ForestEdge(float x) =>
            97f + 5f * (Noise(s_seed + 7, x, 0.5f, 13f) - 0.5f) + 2f * (Noise(s_seed + 8, x, 0.5f, 4f) - 0.5f) - 5f * Smooth(70f, 95f, x);

        public bool InForest(float x, float z) => z > ForestEdge(x) + 0.5f;

        /// <summary>Whether the point lies on a road or a lane, or within the margin of one.</summary>
        public bool OnRoad(float x, float z, float margin) => RoadDistance(x, z) < margin;

        /// <summary>Metres from the point to the nearest road's or lane's edge, below 0 on it.</summary>
        public float RoadDistance(float x, float z)
        {
            float least = float.MaxValue;
            for (int r = 0; r < _roads.Length; r++)
                least = MathF.Min(least, PolylineDistance(_roads[r], x, z) - _roadHalf[r]);
            return least;
        }

        public bool InField(float x, float z, float margin) => s_fields.Any(f => f.Holds(x, z, margin));

        public bool InGarden(float x, float z, float margin) => Gardens.Any(g => Within(x, z, g.MinX, g.MaxX, g.MinZ, g.MaxZ, margin));

        /// <summary>Whether the point lies on a garden's bed or on the cabbage bed beside cottage_low, or within the margin of one.</summary>
        public bool InBed(float x, float z, float margin) =>
            Gardens.Any(g => Within(x, z, g.Bed.MinX, g.Bed.MaxX, g.Bed.MinZ, g.Bed.MaxZ, margin)) || Within(x, z, 1.5f, 8f, 62f, 65f, margin);

        private static bool Within(float x, float z, float minX, float maxX, float minZ, float maxZ, float margin) =>
            x > minX - margin && x < maxX + margin && z > minZ - margin && z < maxZ + margin;

        public bool NearBuilding(float x, float z, float margin) => _buildings.Any(b => b.Distance(x, z) < margin);

        /// <summary>The ground's steepest rise per metre about the point (a cliff_rock riser above 0.9).</summary>
        public float Slope(float x, float z)
        {
            const float H = 0.5f;
            float dx = (Height(x + H, z) - Height(x - H, z)) / (2f * H), dz = (Height(x, z + H) - Height(x, z - H)) / (2f * H);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>A building's pad: the land's level under its middle.</summary>
        public float PadLevel(PlacedBuilding building) => Base(building.X, building.Z) * Smooth(0f, 2.4f, TownDistance(building.X, building.Z));

        // ---- the height ----

        /// <summary>
        /// The land before the stream, the pads and the roads: gentle relief rising outward from the wall, the north's
        /// and the east's slopes, the windmill's hill, the orchard's slope and the terraces' steps; the map's south and
        /// west edges falling away into the mist.
        /// </summary>
        private static float Base(float x, float z)
        {
            float d = BoxDistance(x, z, 0f, TownMax, 0f, TownMax);
            float h = 0.7f * Smooth(6f, 40f, d) + 0.9f * Smooth(84f, 116f, z) + 0.6f * Smooth(82f, 116f, x);
            h += Smooth(3f, 18f, d) * ((Noise(s_seed, x, z, 23f) - 0.5f) * 1.5f + (Noise(s_seed + 1, x, z, 7f) - 0.5f) * 0.4f);
            float hill = SceneryDresser.Distance(x, z, WindmillX, WindmillZ);
            h += 4.4f * (1f - Smooth(4.5f, 17f, hill));
            h += 0.9f * Smooth(78.5f, 90f, x) * Smooth(-6f, 2f, z) * Smooth(64f, 56f, z);

            // The terraces: the land stepped up eastward in three risers 0.85 m high, each 1 m across.
            float terrace = Terrace(x, z);
            if (terrace > 0f)
            {
                for (int k = 0; k < Risers; k++)
                    h += terrace * 0.85f * Smooth(RiserX(k, z), RiserX(k, z) + 1f, x);
            }
            // The south and west edges, which the overview looks at, fall away into the mist; the stream falls with them.
            return h - 9f * (1f - Smooth(0.2f, 6f, MathF.Min(x, z) - RingMin));
        }

        /// <summary>How much of the terraces' stepping the land takes at a point: 1 over most of x 93-120, z -27 to 54.</summary>
        private static float Terrace(float x, float z) => Smooth(88f, 93f, x) * Smooth(62f, 54f, z) * Smooth(-31f, -27f, z);

        /// <summary>
        /// Where the terraces' riser <paramref name="k" /> starts at z: about every 7 m east of x 95, on a whole metre,
        /// jogging a metre or two every 6 m of z, so the riser's rock (one 1 m band of the ground's triangles) keeps
        /// straight edges.
        /// </summary>
        private static float RiserX(int k, float z) =>
            95f + 7f * k + MathF.Floor(2.99f * Noise(s_seed + 5 + k, 0.5f, MathF.Floor(z / 6f) * 6f + 3f, 13f));

        /// <summary>Whether the point lies on a terrace's riser, where the stepped land wears rock.</summary>
        private static bool OnRiser(float x, float z)
        {
            if (Terrace(x, z) < 0.6f) return false;
            for (int k = 0; k < Risers; k++)
            {
                if (x >= RiserX(k, z) && x < RiserX(k, z) + 1f) return true;
            }
            return false;
        }

        /// <summary>
        /// The ground: the land, levelled under each building and at the bridge's ends, cut down to the stream's bed and
        /// banks, worn a few centimetres along the roads, and laid to exactly y 0 along the town's walls.
        /// </summary>
        public float Height(float x, float z)
        {
            float h = Base(x, z);
            foreach (PlacedBuilding b in _buildings)
            {
                float reach = b.Distance(x, z);
                if (reach < 3.5f) h += (PadLevel(b) - h) * (1f - Smooth(0.6f, 3.5f, reach));
            }
            foreach (float end in new[] { BridgeX - 6f, BridgeX + 6f })
            {
                float reach = BoxDistance(x, z, end - 1.5f, end + 1.5f, LaneZ - 2.2f, LaneZ + 2.2f);
                if (reach < 3f) h += (BridgeLevel - h) * (1f - Smooth(0f, 3f, reach));
            }

            (float s, int i, float t) = Nearest(x, z);
            if (s < StreamHalf + BankRun)
            {
                float w = _level[i] + (_level[i + 1] - _level[i]) * t;
                h = s < StreamHalf
                    ? w - StreamDepth * (1f - s * s / (StreamHalf * StreamHalf)) - 0.02f
                    : MathF.Min(h, w + (h - w) * Smooth(StreamHalf, StreamHalf + BankRun, s));
            }
            else
            {
                h -= 0.06f * (1f - Smooth(-0.6f, 0.4f, RoadDistance(x, z)));
            }
            return h * Smooth(0f, 2.4f, TownDistance(x, z));
        }

        // ---- the material ----

        /// <summary>
        /// The ground's materials (the roads' and the banks' dirt is drawn over it, <see cref="Overlay" />): dirt under the
        /// stream and on worn patches by the gate, the well and the mill's door; each field's crop ground (wheat field under the standing wheat, tilled soil
        /// under the vegetables and the ploughed field); tilled soil under the garden beds; rock on the terraces' risers;
        /// forest floor under the dark forest; meadow elsewhere.
        /// </summary>
        public string Material(float x, float z)
        {
            if (StreamDistance(x, z) < StreamHalf || Worn(x, z)) return Dirt;
            foreach (Field f in s_fields)
            {
                if (f.Holds(x, z))
                    return f.Kind switch { FieldKind.Wheat => Crop, FieldKind.Hay => Meadow, _ => Soil };
            }
            if (InBed(x, z, 0f)) return Soil;
            if (OnRiser(x, z)) return Rock;
            return InForest(x, z) ? Floor : Meadow;
        }

        /// <summary>Worn earth: round the gate's mouth, the well and the carts, and before each garden's shed.</summary>
        private static bool Worn(float x, float z)
        {
            float n = Noise(s_seed + 3, x, z, 2.5f);
            float gate = ((x - 15.5f) / 8f) * ((x - 15.5f) / 8f) + ((z - 63f) / 3.6f) * ((z - 63f) / 3.6f);
            float well = SceneryDresser.Distance(x, z, 20.4f, 65.6f) / 3f;
            float mill = ((x - 72f) / 5f) * ((x - 72f) / 5f) + ((z - 35.6f) / 2.6f) * ((z - 35.6f) / 2.6f);
            return MathF.Min(MathF.Min(gate, well * well), mill) < 0.7f + 0.5f * n;
        }

        // ---- the course ----

        private (float Distance, int Index, float T) Nearest(float x, float z)
        {
            float best = float.MaxValue, bestT = 0f;
            int bestI = 0;
            for (int i = 0; i + 1 < Course.Count; i++)
            {
                (float ax, float az) = Course[i];
                (float bx, float bz) = Course[i + 1];
                if (MathF.Abs(x - ax) > best + 2f && MathF.Abs(x - bx) > best + 2f) continue;
                float vx = bx - ax, vz = bz - az;
                float t = Math.Clamp(((x - ax) * vx + (z - az) * vz) / (vx * vx + vz * vz), 0f, 1f);
                float dx = x - (ax + vx * t), dz = z - (az + vz * t), distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance < best) (best, bestI, bestT) = (distance, i, t);
            }
            return (best, bestI, bestT);
        }

        /// <summary>
        /// A strip of triangles along the line, from <paramref name="from" /> to <paramref name="to" /> metres to its left
        /// (negative: its right) in <paramref name="across" /> rows, each vertex <see cref="DirtLift" /> over the ground.
        /// </summary>
        private void Band(List<Polygon> polygons, IReadOnlyList<(float X, float Z)> line, float from, float to, int across)
        {
            var rows = new (float X, float Y, float Z)[line.Count][];
            for (int i = 0; i < line.Count; i++)
            {
                (float ax, float az) = line[Math.Max(0, i - 1)];
                (float bx, float bz) = line[Math.Min(line.Count - 1, i + 1)];
                float length = SceneryDresser.Distance(ax, az, bx, bz), nx = -(bz - az) / length, nz = (bx - ax) / length;
                rows[i] = new (float X, float Y, float Z)[across + 1];
                for (int k = 0; k <= across; k++)
                {
                    float off = from + (to - from) * k / across, x = line[i].X + nx * off, z = line[i].Z + nz * off;
                    rows[i][k] = (x, Height(x, z) + DirtLift, z);
                }
            }
            for (int i = 0; i + 1 < line.Count; i++)
            {
                for (int k = 0; k < across; k++)
                {
                    polygons.Add([rows[i][k], rows[i][k + 1], rows[i + 1][k + 1]]);
                    polygons.Add([rows[i][k], rows[i + 1][k + 1], rows[i + 1][k]]);
                }
            }
        }

        /// <summary>A Catmull-Rom curve through the points, sampled about every <paramref name="spacing" /> metres.</summary>
        private static (float X, float Z)[] Spline(IReadOnlyList<(float X, float Z)> points, float spacing)
        {
            List<(float X, float Z)> curve = [];
            for (int i = 0; i + 1 < points.Count; i++)
            {
                (float X, float Z) p0 = points[Math.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Math.Min(points.Count - 1, i + 2)];
                int steps = Math.Max(1, (int)MathF.Ceiling(SceneryDresser.Distance(p1.X, p1.Z, p2.X, p2.Z) / spacing));
                for (int k = 0; k < steps; k++)
                {
                    float t = (float)k / steps, t2 = t * t, t3 = t2 * t;
                    float x = 0.5f * (2f * p1.X + (p2.X - p0.X) * t + (2f * p0.X - 5f * p1.X + 4f * p2.X - p3.X) * t2 + (3f * p1.X - p0.X - 3f * p2.X + p3.X) * t3);
                    float z = 0.5f * (2f * p1.Z + (p2.Z - p0.Z) * t + (2f * p0.Z - 5f * p1.Z + 4f * p2.Z - p3.Z) * t2 + (3f * p1.Z - p0.Z - 3f * p2.Z + p3.Z) * t3);
                    curve.Add((x, z));
                }
            }
            curve.Add(points[^1]);
            return [.. curve];
        }
    }
}
