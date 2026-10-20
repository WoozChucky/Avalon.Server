using System.Globalization;
using Polygon = System.Collections.Generic.IReadOnlyList<(float X, float Y, float Z)>;

namespace Avalon.ChunkGen;

/// <summary>
/// The drawn ground of a scenery piece, never walkable: a heightfield (<paramref name="Height" />, y at an (x, z) in
/// metres) sampled every <see cref="Step" /> over the 30 x 30 m cell, each triangle wearing the
/// <paramref name="Material" /> at its centroid, plus rough rock faces (<see cref="Walls" />) and water and mist
/// (<see cref="Surfaces" />). A cliff or a ravine is the heightfield's own slope behind the face (a 1 m band) with a
/// wall over it: no hole is cut in the heightfield. The functions take piece-local metres, or world metres in a scene
/// the composer cuts (SceneryComposer).
/// </summary>
public sealed record SceneryTerrain(Func<float, float, float> Height, Func<float, float, string> Material)
{
    /// <summary>The ground grid's spacing in metres: 31 x 31 vertices a piece.</summary>
    public const float Step = 1f;

    /// <summary>
    /// Every material scenery draws with, in the order a piece's ground writes its groups. The client's palette
    /// (its vendor script) must know each one.
    /// </summary>
    public static readonly string[] Palette = ["forest_floor", "meadow", "dirt_road", "tilled_soil", "wheat_field", "cliff_rock", "water", "mist"];

    /// <summary>The rough rock faces, none unless listed.</summary>
    public IReadOnlyList<SceneryWall> Walls { get; init; } = [];

    /// <summary>The water and mist, none unless listed.</summary>
    public IReadOnlyList<ScenerySurface> Surfaces { get; init; } = [];
}

/// <summary>
/// A rough rock face standing from <paramref name="Bottom" /> to <paramref name="Top" /> (y, metres) along
/// <paramref name="Outline" /> (x, z points; <paramref name="Closed" /> joins the last back to the first). The outline is
/// resampled every <see cref="Spacing" />, with rows at most <see cref="RowHeight" /> apart, and every vertex is pushed
/// up to <see cref="Roughness" /> out along the outline's normal by a value hashed from its position, so the face is
/// rough and a rerun is identical. The face looks along (dz, -dx) for an outline step (dx, dz): a loop running
/// clockwise seen from above (+X east, +Z north) looks into it, as a ravine's sides do, and one running
/// counter-clockwise looks out of it, as a plateau's face does.
/// </summary>
public sealed record SceneryWall(IReadOnlyList<(float X, float Z)> Outline, bool Closed, float Bottom, float Top, string Material)
{
    /// <summary>The longest step between two columns of the face, in metres.</summary>
    public const float Spacing = 0.5f;

    /// <summary>The tallest step between two rows of the face, in metres.</summary>
    public const float RowHeight = 1f;

    /// <summary>The furthest a vertex is pushed out of the outline, in metres.</summary>
    public const float Roughness = 0.4f;

    /// <summary>
    /// The face's vertices and triangles (0-based into the vertices), each triangle wound so (b - a) x (c - a) looks the
    /// way the face does. Refuses (InvalidOperationException) an outline of fewer than two distinct points (three for a
    /// closed one), a coordinate that is not a number, or a top not above the bottom.
    /// </summary>
    internal (IReadOnlyList<(float X, float Y, float Z)> Vertices, IReadOnlyList<(int A, int B, int C)> Triangles) Mesh()
    {
        if (!(float.IsFinite(Bottom) && float.IsFinite(Top) && Top > Bottom))
            throw new InvalidOperationException($"a {Material} wall stands from y {ForestText.N(Bottom)} to {ForestText.N(Top)}, not upward");
        if (Outline.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Z)))
            throw new InvalidOperationException($"a {Material} wall has an outline point that is not a position");

        List<(float X, float Z, float NX, float NZ)> columns = Columns();
        int rows = Math.Max(1, (int)MathF.Ceiling((Top - Bottom) / RowHeight - 0.0001f));
        var vertices = new List<(float X, float Y, float Z)>(columns.Count * (rows + 1));
        foreach ((float x, float z, float nx, float nz) in columns)
        {
            for (int r = 0; r <= rows; r++)
            {
                float y = Bottom + (Top - Bottom) * r / rows;
                float push = Roughness * Hash(x, y, z);
                vertices.Add((x + nx * push, y, z + nz * push));
            }
        }

        // Column i's row r is vertex i * (rows + 1) + r. A quad (a, b) along the outline, (d, c) a row up, is wound
        // (a, c, b) and (a, d, c): (c - a) x (b - a) is (dz, 0, -dx) for an outline step (dx, dz), the face's look.
        var triangles = new List<(int A, int B, int C)>();
        int spans = Closed ? columns.Count : columns.Count - 1;
        for (int i = 0; i < spans; i++)
        {
            int j = (i + 1) % columns.Count;
            for (int r = 0; r < rows; r++)
            {
                int a = i * (rows + 1) + r, b = j * (rows + 1) + r, c = b + 1, d = a + 1;
                triangles.Add((a, c, b));
                triangles.Add((a, d, c));
            }
        }
        return (vertices, triangles);
    }

    /// <summary>The outline resampled every <see cref="Spacing" /> at most, each point with the face's unit normal there.</summary>
    private List<(float X, float Z, float NX, float NZ)> Columns()
    {
        List<(float X, float Z)> points = [];
        foreach ((float X, float Z) p in Outline)
        {
            if (points.Count == 0 || Distance(points[^1], p) > 0.001f) points.Add(p);
        }
        if (Closed && points.Count > 1 && Distance(points[^1], points[0]) <= 0.001f) points.RemoveAt(points.Count - 1);
        if (points.Count < (Closed ? 3 : 2))
            throw new InvalidOperationException($"a {Material} wall's outline has {points.Count} distinct points, fewer than {(Closed ? 3 : 2)}");

        int segments = Closed ? points.Count : points.Count - 1;
        var normals = new (float X, float Z)[segments];
        for (int s = 0; s < segments; s++)
        {
            (float X, float Z) p = points[s], q = points[(s + 1) % points.Count];
            float length = Distance(p, q);
            normals[s] = ((q.Z - p.Z) / length, -(q.X - p.X) / length);
        }

        List<(float X, float Z, float NX, float NZ)> columns = [];
        for (int s = 0; s < segments; s++)
        {
            (float X, float Z) p = points[s], q = points[(s + 1) % points.Count];
            int steps = Math.Max(1, (int)MathF.Ceiling(Distance(p, q) / Spacing - 0.0001f));
            for (int k = 0; k < steps; k++)
            {
                (float nx, float nz) = normals[s];
                if (k == 0 && (Closed || s > 0))
                    (nx, nz) = Joint(normals[(s + segments - 1) % segments], normals[s]);
                float t = (float)k / steps;
                columns.Add((p.X + (q.X - p.X) * t, p.Z + (q.Z - p.Z) * t, nx, nz));
            }
        }
        if (!Closed)
            columns.Add((points[^1].X, points[^1].Z, normals[^1].X, normals[^1].Z));
        return columns;
    }

    /// <summary>The two segments' normals averaged, or the second's where they cancel (an outline turning back on itself).</summary>
    private static (float X, float Z) Joint((float X, float Z) before, (float X, float Z) after)
    {
        float x = before.X + after.X, z = before.Z + after.Z, length = MathF.Sqrt(x * x + z * z);
        return length < 0.001f ? after : (x / length, z / length);
    }

    /// <summary>0-1 from the position rounded to the millimetre (ForestDresser.StableSeed): the same point pushes the same on any machine.</summary>
    private static float Hash(float x, float y, float z) =>
        ForestDresser.StableSeed($"{Mm(x)} {Mm(y)} {Mm(z)}") % 1001 / 1000f;

    private static string Mm(float value)
    {
        double rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static float Distance((float X, float Z) a, (float X, float Z) b) =>
        MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
}

/// <summary>
/// Water or mist drawn over the ground: convex polygons (<see cref="Strip" />, or the cut of one by SceneryComposer) or
/// an area of 0.5 m grid squares whose centres a test holds (<see cref="Area" />). The obj format the client reads
/// carries no flow, so water's flow is its slope: a flowing surface slopes down along its flow by at least
/// <see cref="MinFlowSlope" />, and a still one (an area) is flat.
/// </summary>
public sealed record ScenerySurface
{
    /// <summary>The side of an area's grid squares, in metres.</summary>
    public const float AreaGrid = 0.5f;

    /// <summary>The least drop a strip makes per metre along its flow: 0.5 %.</summary>
    public const float MinFlowSlope = 0.005f;

    private readonly Polygon[]? _polygons;
    private readonly Func<float, float, bool>? _inside;
    private readonly float _y;

    private ScenerySurface(string material, Polygon[]? polygons, Func<float, float, bool>? inside, float y)
    {
        Material = material;
        _polygons = polygons;
        _inside = inside;
        _y = y;
    }

    public string Material { get; }

    /// <summary>
    /// A stream's water: a band <paramref name="halfWidth" /> either side of <paramref name="centre" /> (x, y, z points
    /// in flow order), level across, at each point's y along. Refuses (InvalidOperationException) fewer than two
    /// points, a half-width that is not a number above 0, two points on one spot, a coordinate that is not a number,
    /// a step that drops less than <see cref="MinFlowSlope" /> of its length (flowing water runs downhill), and a bend
    /// too tight for the half-width, where an edge folds back over itself.
    /// </summary>
    public static ScenerySurface Strip(IReadOnlyList<(float X, float Y, float Z)> centre, float halfWidth, string material)
    {
        if (centre.Count < 2)
            throw new InvalidOperationException($"a {material} strip needs two centre points or more, not {centre.Count}");
        if (!(float.IsFinite(halfWidth) && halfWidth > 0f))
            throw new InvalidOperationException($"a {material} strip has half-width {ForestText.N(halfWidth)}, not a number above 0");
        if (centre.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)))
            throw new InvalidOperationException($"a {material} strip has a centre point that is not a position");
        for (int i = 1; i < centre.Count; i++)
        {
            (float X, float Y, float Z) a = centre[i - 1], b = centre[i];
            float run = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
            if (run < 0.001f)
                throw new InvalidOperationException($"a {material} strip has two centre points at ({ForestText.N(b.X)}, {ForestText.N(b.Z)})");
            if (a.Y - b.Y < MinFlowSlope * run - 0.0001f)
            {
                throw new InvalidOperationException(
                    $"a {material} strip drops {ForestText.N(a.Y - b.Y)} m over {ForestText.N(run)} m to "
                    + $"({ForestText.N(b.X)}, {ForestText.N(b.Z)}): flowing water slopes down at least 0.5 %");
            }
        }

        var left = new (float X, float Y, float Z)[centre.Count];
        var right = new (float X, float Y, float Z)[centre.Count];
        for (int i = 0; i < centre.Count; i++)
        {
            (float X, float Y, float Z) from = centre[Math.Max(0, i - 1)], to = centre[Math.Min(centre.Count - 1, i + 1)];
            float tx = to.X - from.X, tz = to.Z - from.Z, length = MathF.Sqrt(tx * tx + tz * tz);
            float px = -tz / length * halfWidth, pz = tx / length * halfWidth;
            (float x, float y, float z) = centre[i];
            left[i] = (x + px, y, z + pz);
            right[i] = (x - px, y, z - pz);
        }

        Unfolded(left, "left", centre, material);
        Unfolded(right, "right", centre, material);

        List<Polygon> polygons = [];
        for (int i = 1; i < centre.Count; i++)
        {
            polygons.Add([left[i - 1], right[i - 1], right[i]]);
            polygons.Add([left[i - 1], right[i], left[i]]);
        }
        return new ScenerySurface(material, [.. polygons], null, 0f);
    }

    /// <summary>
    /// Refuses (InvalidOperationException naming the strip by its material and first centre point) an edge that folds
    /// back over itself: a step of it that does not run the way its centre step runs, as a bend too tight for the
    /// half-width makes the inner edge do, or two consecutive steps of it that do not run the same way (each dot
    /// product 0 or less).
    /// </summary>
    private static void Unfolded(
        (float X, float Y, float Z)[] edge, string side, IReadOnlyList<(float X, float Y, float Z)> centre, string material)
    {
        for (int i = 1; i < edge.Length; i++)
        {
            float ex = edge[i].X - edge[i - 1].X, ez = edge[i].Z - edge[i - 1].Z;
            bool against = ex * (centre[i].X - centre[i - 1].X) + ez * (centre[i].Z - centre[i - 1].Z) <= 0f;
            bool back = i >= 2 && ex * (edge[i - 1].X - edge[i - 2].X) + ez * (edge[i - 1].Z - edge[i - 2].Z) <= 0f;
            if (against || back)
            {
                throw new InvalidOperationException(
                    $"the {material} strip from ({ForestText.N(centre[0].X)}, {ForestText.N(centre[0].Z)}) bends too tightly for its "
                    + $"half-width: its {side} edge folds back at ({ForestText.N(edge[i - 1].X)}, {ForestText.N(edge[i - 1].Z)})");
            }
        }
    }

    /// <summary>
    /// A pool or a mist layer: flat at <paramref name="y" />, the squares of a <see cref="AreaGrid" /> grid (on whole
    /// multiples of it) whose centres <paramref name="inside" /> holds.
    /// </summary>
    public static ScenerySurface Area(Func<float, float, bool> inside, float y, string material)
    {
        if (!float.IsFinite(y))
            throw new InvalidOperationException($"a {material} area stands at y {ForestText.N(y)}, not a number");
        return new ScenerySurface(material, null, inside, y);
    }

    /// <summary>A surface of convex polygons as given (a cut of another, SceneryComposer).</summary>
    internal static ScenerySurface Of(string material, IReadOnlyList<Polygon> polygons) => new(material, [.. polygons], null, 0f);

    /// <summary>
    /// The surface's convex polygons within the rectangle, clipped to it (Sutherland-Hodgman, y carried along each
    /// cut edge); a polygon outside it is left out, so the list may be empty.
    /// </summary>
    internal IReadOnlyList<Polygon> Within(float minX, float minZ, float maxX, float maxZ)
    {
        List<Polygon> within = [];
        if (_inside is not null)
        {
            int x0 = (int)MathF.Floor(minX / AreaGrid), x1 = (int)MathF.Ceiling(maxX / AreaGrid);
            int z0 = (int)MathF.Floor(minZ / AreaGrid), z1 = (int)MathF.Ceiling(maxZ / AreaGrid);
            for (int gx = x0; gx < x1; gx++)
            {
                for (int gz = z0; gz < z1; gz++)
                {
                    float ax = gx * AreaGrid, az = gz * AreaGrid;
                    if (!_inside(ax + AreaGrid / 2f, az + AreaGrid / 2f)) continue;
                    float sx0 = Math.Max(ax, minX), sx1 = Math.Min(ax + AreaGrid, maxX);
                    float sz0 = Math.Max(az, minZ), sz1 = Math.Min(az + AreaGrid, maxZ);
                    if (sx1 > sx0 && sz1 > sz0)
                        within.Add([(sx0, _y, sz0), (sx1, _y, sz0), (sx1, _y, sz1), (sx0, _y, sz1)]);
                }
            }
            return within;
        }

        foreach (Polygon polygon in _polygons!)
        {
            List<(float X, float Y, float Z)> clipped = [.. polygon];
            clipped = Clip(clipped, p => p.X - minX);
            clipped = Clip(clipped, p => maxX - p.X);
            clipped = Clip(clipped, p => p.Z - minZ);
            clipped = Clip(clipped, p => maxZ - p.Z);
            if (clipped.Count >= 3) within.Add(clipped);
        }
        return within;
    }

    /// <summary>One Sutherland-Hodgman pass: the polygon's part where <paramref name="side" /> is 0 or more.</summary>
    private static List<(float X, float Y, float Z)> Clip(List<(float X, float Y, float Z)> polygon, Func<(float X, float Y, float Z), float> side)
    {
        List<(float X, float Y, float Z)> kept = [];
        for (int i = 0; i < polygon.Count; i++)
        {
            (float X, float Y, float Z) a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            float da = side(a), db = side(b);
            if (da >= 0f) kept.Add(a);
            if ((da >= 0f) != (db >= 0f))
            {
                float t = da / (da - db);
                kept.Add((a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t));
            }
        }
        return kept;
    }
}
