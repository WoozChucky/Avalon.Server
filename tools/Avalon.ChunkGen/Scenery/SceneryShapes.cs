using Outline = System.Collections.Generic.IReadOnlyList<(float X, float Z)>;

namespace Avalon.ChunkGen;

/// <summary>
/// The plane geometry scenery terrain is built from, in piece-local or world metres: value noise, smoothstep, outlines
/// (inside, distance, signed area, convexity, inset), segment distances and boxes on the ground. Only arithmetic and
/// square roots, which IEEE 754 rounds the same on every machine, so a terrain written on one machine is
/// byte-identical on another.
/// </summary>
internal static class SceneryShapes
{
    private const float Cell = ChunkPiece.CellSize;

    /// <summary>
    /// Value noise 0-1 on a lattice of the cell size: hashed corners, smoothstep between them (no trigonometry, so
    /// every machine agrees).
    /// </summary>
    internal static float Noise(int seed, float x, float z, float size)
    {
        float fx = x / size, fz = z / size;
        int ix = (int)MathF.Floor(fx), iz = (int)MathF.Floor(fz);
        float tx = fx - ix, tz = fz - iz;
        tx = tx * tx * (3f - 2f * tx);
        tz = tz * tz * (3f - 2f * tz);
        float a = Lattice(seed, ix, iz), b = Lattice(seed, ix + 1, iz), c = Lattice(seed, ix, iz + 1), e = Lattice(seed, ix + 1, iz + 1);
        return a + (b - a) * tx + (c - a) * tz + (a - b - c + e) * tx * tz;
    }

    internal static float Lattice(int seed, int x, int z)
    {
        uint h = unchecked((uint)seed * 0x9E3779B9u ^ (uint)x * 0x85EBCA6Bu ^ (uint)z * 0xC2B2AE35u);
        h ^= h >> 16;
        h = unchecked(h * 0x7FEB352Du);
        h ^= h >> 15;
        h = unchecked(h * 0x846CA68Bu);
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777216f;
    }

    internal static float Smooth(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    internal static float EdgeDistance(float x, float z) => MathF.Min(MathF.Min(x, z), MathF.Min(Cell - x, Cell - z));

    /// <summary>Metres from the point to the box (x <paramref name="minX" /> to <paramref name="maxX" />, z likewise), 0 inside it.</summary>
    internal static float BoxDistance(float x, float z, float minX, float maxX, float minZ, float maxZ)
    {
        float dx = MathF.Max(0f, MathF.Max(minX - x, x - maxX)), dz = MathF.Max(0f, MathF.Max(minZ - z, z - maxZ));
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    internal static bool Inside(Outline outline, float x, float z)
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
    internal static float OutlineDistance(Outline outline, float x, float z)
    {
        float least = float.MaxValue;
        for (int i = 0; i < outline.Count; i++)
            least = MathF.Min(least, SegmentDistance(x, z, outline[i], outline[(i + 1) % outline.Count]));
        return least;
    }

    /// <summary>Metres from the point to the open line's nearest segment.</summary>
    internal static float PolylineDistance(Outline line, float x, float z)
    {
        float least = float.MaxValue;
        for (int i = 1; i < line.Count; i++)
            least = MathF.Min(least, SegmentDistance(x, z, line[i - 1], line[i]));
        return least;
    }

    internal static float SegmentDistance(float x, float z, (float X, float Z) a, (float X, float Z) b)
    {
        float vx = b.X - a.X, vz = b.Z - a.Z;
        float t = Math.Clamp(((x - a.X) * vx + (z - a.Z) * vz) / (vx * vx + vz * vz), 0f, 1f);
        return SceneryDresser.Distance(x, z, a.X + vx * t, a.Z + vz * t);
    }

    /// <summary>
    /// Twice-signed area over two: positive for an outline running counter-clockwise seen from above.
    /// </summary>
    internal static float SignedArea(Outline outline)
    {
        float sum = 0f;
        for (int i = 0; i < outline.Count; i++)
        {
            (float X, float Z) a = outline[i], b = outline[(i + 1) % outline.Count];
            sum += a.X * b.Z - b.X * a.Z;
        }
        return sum / 2f;
    }

    internal static (float X, float Z) Centroid(Outline outline) => (outline.Average(p => p.X), outline.Average(p => p.Z));

    /// <summary>Refuses an outline that is not convex and counter-clockwise (every turn to the left).</summary>
    internal static void Convex(string name, Outline outline)
    {
        for (int i = 0; i < outline.Count; i++)
        {
            (float X, float Z) a = outline[i], b = outline[(i + 1) % outline.Count], c = outline[(i + 2) % outline.Count];
            if ((b.X - a.X) * (c.Z - b.Z) - (b.Z - a.Z) * (c.X - b.X) <= 0f)
                throw new InvalidOperationException($"{name}: the outline turns right at ({ForestText.N(b.X)}, {ForestText.N(b.Z)}): it must be convex, counter-clockwise");
        }
    }

    /// <summary>
    /// A convex counter-clockwise outline with every side moved <paramref name="by" /> metres in, its corners where the
    /// moved sides meet.
    /// </summary>
    internal static Outline Inset(Outline outline, float by)
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

    /// <summary>
    /// A box on the ground, square to the axes, in metres (a field, a building's footprint): x <paramref name="MinX" />
    /// to <paramref name="MaxX" />, z <paramref name="MinZ" /> to <paramref name="MaxZ" />.
    /// </summary>
    internal record GroundBox(float MinX, float MaxX, float MinZ, float MaxZ)
    {
        /// <summary>Whether the point lies inside the box grown by <paramref name="margin" /> on every side, its edge excluded.</summary>
        public bool Holds(float x, float z, float margin = 0f) =>
            x > MinX - margin && x < MaxX + margin && z > MinZ - margin && z < MaxZ + margin;

        /// <summary>Metres from the point to the box, 0 inside it.</summary>
        public float Distance(float x, float z) => BoxDistance(x, z, MinX, MaxX, MinZ, MaxZ);
    }
}
