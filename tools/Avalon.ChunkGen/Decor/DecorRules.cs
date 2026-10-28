namespace Avalon.ChunkGen;

/// <summary>
/// What a player sees and what they collide with agree (spec of 2026-10-17). Every solid decor item's footprint, its
/// model's box from the bounds table scaled and turned by its yaw, lies within <see cref="Tolerance" /> of its
/// building's non-walkable proxies (a <see cref="Walls" /> item: of the square's wall boxes), so nothing drawn stands
/// where a player can walk. And every <see cref="Material.Collision" /> proxy, which the client does not draw, has
/// solid decor on it, and one standing at body height (below <see cref="BodyHeight" />) reaches no more than
/// <see cref="Tolerance" /> past the decor drawn over its -X and -Z faces, the sides the fixed camera sees, so no
/// invisible wall shows. A square whose walls or floor decor draws (<see cref="TownSquare.WallsAsDecor" />,
/// <see cref="TownSquare.Paved" />) has every wall box covered by its wall sections and the whole floor tiled. Flat
/// ground tiles and thin hanging dressing (<see cref="DressingPrefixes" />) are not solid.
/// </summary>
public static class DecorRules
{
    public const float Tolerance = 0.15f;

    /// <summary>A proxy whose base is below this is one a player walks into; decor below it is what they see there.</summary>
    public const float BodyHeight = 1.9f;

    /// <summary>The building a square's own wall sections are placed under: they are checked against its wall boxes.</summary>
    public const string Walls = "Walls";

    /// <summary>The building a square's ground tiles are placed under: they tile its floor.</summary>
    public const string Paving = "Paving";

    /// <summary>Model name prefixes of what is flat on the ground or hangs thin on a wall, never collided with.</summary>
    public static readonly string[] DressingPrefixes = ["Prop_Vine", "WindowShutters_", "Window_", "Floor_", "Prop_ExteriorBorder_", "HoleCover_"];

    private static readonly Lazy<IReadOnlyDictionary<string, ModelBounds>> s_kit = new(KitBounds.Load, isThreadSafe: true);

    /// <summary>
    /// The item's model box scaled by its scale and turned by its yaw about the model's origin (as
    /// <see cref="TownBuilding.Place" /> turns a recipe) and moved to the item's position: chunk-local metres. A yaw off
    /// a quarter turn (a doodad-drawn prop's, <see cref="TownSquare.DoodadDrawn" />) gives the bounds of the turned box.
    /// A model the bounds table does not have, or a yaw outside 0-359, is refused (InvalidOperationException).
    /// </summary>
    public static (float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ) WorldBounds(DecorItem item)
    {
        if (!s_kit.Value.TryGetValue(item.Model, out ModelBounds? b))
            throw new InvalidOperationException($"the kit's bounds table has no model '{item.Model}'");
        if (item.Yaw is < 0 or > 359)
            throw new InvalidOperationException($"{item.Model} has yaw {item.Yaw}, not 0-359");

        float s = item.Scale;
        (float minX, float maxX, float minZ, float maxZ) = item.Yaw % 90 == 0
            ? TownBuilding.Footprint(b.MinX * s, b.MaxX * s, b.MinZ * s, b.MaxZ * s, item.X, item.Z, item.Yaw / 90)
            : Turned(b.MinX * s, b.MaxX * s, b.MinZ * s, b.MaxZ * s, item.X, item.Z, item.Yaw);
        return (minX, maxX, item.Y + b.MinY * s, item.Y + b.MaxY * s, minZ, maxZ);
    }

    /// <summary>
    /// The bounds of a box turned clockwise seen from above by <paramref name="yaw" /> degrees about the model's origin
    /// (yaw 90 takes local +Z to +X, as <see cref="TownBuilding.Footprint" />'s quarter turns do) and moved to the anchor.
    /// </summary>
    private static (float MinX, float MaxX, float MinZ, float MaxZ) Turned(
        float minX, float maxX, float minZ, float maxZ, float anchorX, float anchorZ, int yaw)
    {
        double radians = yaw * Math.PI / 180.0;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        double loX = double.MaxValue, hiX = double.MinValue, loZ = double.MaxValue, hiZ = double.MinValue;
        foreach ((float x, float z) in new[] { (minX, minZ), (minX, maxZ), (maxX, minZ), (maxX, maxZ) })
        {
            double tx = x * cos + z * sin, tz = -x * sin + z * cos;
            loX = Math.Min(loX, tx);
            hiX = Math.Max(hiX, tx);
            loZ = Math.Min(loZ, tz);
            hiZ = Math.Max(hiZ, tz);
        }
        return ((float)(anchorX + loX), (float)(anchorX + hiX), (float)(anchorZ + loZ), (float)(anchorZ + hiZ));
    }

    /// <summary>Whether a player could collide with the item: anything but ground tiles and hanging dressing.</summary>
    public static bool IsSolid(DecorItem item)
    {
        foreach (string prefix in DressingPrefixes)
        {
            if (item.Model.StartsWith(prefix, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>"square: building" for a refusal, or the square alone when the item or piece has no building.</summary>
    public static string Where(string square, string building) => building.Length > 0 ? $"{square}: {building}" : square;

    /// <summary>
    /// Refuses (InvalidOperationException naming the square, the building and the model or part) a solid item outside
    /// its proxies or walls, a solid item of a building with no non-walkable proxy, a collision proxy with no solid
    /// decor on it, a body-height collision proxy reaching past its decor on the camera's side, a wall box its sections
    /// leave bare and a floor its tiles leave bare.
    /// </summary>
    public static void Check(TownSquare square)
    {
        // A doodad-drawn prop (TownSquare.DoodadDrawn) is solid decor here both ways: it must stand inside its proxy, and
        // it draws that proxy, though the decor file does not carry it.
        List<Drawn> solid = [];
        foreach (DecorItem item in square.Drawn)
        {
            if (!IsSolid(item)) continue;
            (float minX, float maxX, float minY, float maxY, float minZ, float maxZ) = WorldBounds(item);
            var footprint = new Rect(minX, maxX, minZ, maxZ);
            string what = $"{Where(square.Name, item.Building)}: {item.Model} at ({item.X}, {item.Z})";

            List<Rect> proxies = item.Building == Walls
                ? [.. square.Walls.Select(w => Rect.Of(w).Grown(Tolerance))]
                : [.. square.Pieces.Where(p => !p.Walkable && p.Building == item.Building).Select(p => Rect.Of(p).Grown(Tolerance))];
            if (proxies.Count == 0)
            {
                throw new InvalidOperationException(item.Building == Walls
                    ? $"{what} is a wall section, but the square has no walls"
                    : $"{what} is solid, but {item.Building} has no non-walkable proxy in the square to collide with");
            }
            if (!Covered(footprint, proxies))
            {
                string against = item.Building == Walls ? "the square's walls" : $"{item.Building}'s proxy";
                throw new InvalidOperationException(
                    $"{what} reaches more than {Tolerance} m outside {against}: a player would walk through what they see");
            }

            solid.Add(new Drawn(item, footprint, minY, maxY));
        }

        if (square.WallsAsDecor)
        {
            List<Rect> sections = [.. solid.Where(d => d.Item.Building == Walls).Select(d => d.Footprint)];
            foreach (WallSegment wall in square.Walls)
            {
                if (!Covered(Rect.Of(wall), sections))
                    throw new InvalidOperationException($"{square.Name}: {wall.Name} is drawn as decor, but its wall sections leave part of it bare");
            }
        }

        if (square.Paved)
        {
            List<Rect> tiles = [.. square.Decor.Where(d => d.Building == Paving).Select(d =>
            {
                (float minX, float maxX, _, _, float minZ, float maxZ) = WorldBounds(d);
                return new Rect(minX, maxX, minZ, maxZ);
            })];
            if (!Covered(new Rect(0f, TownSquare.CellSize, 0f, TownSquare.CellSize), tiles))
                throw new InvalidOperationException($"{square.Name} is paved, but its {Paving} tiles leave part of the floor bare");
        }

        foreach (TownPiece piece in square.Pieces)
        {
            if (piece.Walkable || piece.Material != Material.Collision) continue;
            var proxy = Rect.Of(piece);
            List<Drawn> over = [.. solid.Where(d => d.Footprint.Overlaps(proxy))];
            if (over.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{Where(square.Name, piece.Building)}/{piece.Part} is collision, which the client does not draw, but no solid decor stands on it: an invisible wall");
            }
            if (piece.Y0 >= BodyHeight) continue;

            // What is drawn where a player walks into the proxy: decor overlapping it between its base and body height.
            float from = Math.Max(0f, piece.Y0), to = Math.Min(BodyHeight, piece.Top);
            List<Rect> drawn = [.. over.Where(d => d.MinY < to && d.MaxY > from).Select(d => d.Footprint)];
            (float faceZ0, float faceZ1) = Inset(proxy.MinZ, proxy.MaxZ);
            (float faceX0, float faceX1) = Inset(proxy.MinX, proxy.MaxX);
            float x = Math.Min(proxy.MinX + Tolerance, (proxy.MinX + proxy.MaxX) / 2f);
            float z = Math.Min(proxy.MinZ + Tolerance, (proxy.MinZ + proxy.MaxZ) / 2f);
            if (!Covered(new Rect(x, x, faceZ0, faceZ1), drawn))
                throw new InvalidOperationException(PastDecor(square, piece, "-X"));
            if (!Covered(new Rect(faceX0, faceX1, z, z), drawn))
                throw new InvalidOperationException(PastDecor(square, piece, "-Z"));
        }
    }

    /// <summary>A face's span less the tolerance at each end (the proxy may overreach its decor that much along it too); its middle when shorter.</summary>
    private static (float From, float To) Inset(float min, float max) =>
        max - min > 2 * Tolerance ? (min + Tolerance, max - Tolerance) : ((min + max) / 2f, (min + max) / 2f);

    private static string PastDecor(TownSquare square, TownPiece piece, string side) =>
        $"{Where(square.Name, piece.Building)}/{piece.Part} reaches more than {Tolerance} m past the decor drawn over its {side} face " +
        "at body height: an invisible wall on the camera's side";

    /// <summary>
    /// Whether the boxes cover the area (a line or a point when it has no width). The boxes' edges cut the area into
    /// cells no edge crosses, so each box holds a whole cell or none of it, and testing each cell's centre is exact.
    /// </summary>
    private static bool Covered(Rect area, List<Rect> boxes)
    {
        float[] xs = Centres(area.MinX, area.MaxX, boxes.SelectMany(b => new[] { b.MinX, b.MaxX }));
        float[] zs = Centres(area.MinZ, area.MaxZ, boxes.SelectMany(b => new[] { b.MinZ, b.MaxZ }));
        foreach (float x in xs)
        {
            foreach (float z in zs)
            {
                if (!boxes.Any(b => x >= b.MinX && x <= b.MaxX && z >= b.MinZ && z <= b.MaxZ))
                    return false;
            }
        }
        return true;
    }

    /// <summary>The centres of the spans the edges inside (min, max) cut it into; min itself when the span is empty.</summary>
    private static float[] Centres(float min, float max, IEnumerable<float> edges)
    {
        if (max <= min) return [min];
        float[] cuts = [.. edges.Where(e => e > min && e < max).Append(min).Append(max).Distinct().Order()];
        float[] centres = new float[cuts.Length - 1];
        for (int i = 0; i < centres.Length; i++)
            centres[i] = (cuts[i] + cuts[i + 1]) / 2f;
        return centres;
    }

    /// <summary>A solid decor item, its footprint and its height span.</summary>
    private readonly record struct Drawn(DecorItem Item, Rect Footprint, float MinY, float MaxY);

    private readonly record struct Rect(float MinX, float MaxX, float MinZ, float MaxZ)
    {
        public static Rect Of(TownPiece piece)
        {
            (float minX, float maxX, float minZ, float maxZ) = piece.Bounds;
            return new Rect(minX, maxX, minZ, maxZ);
        }

        public static Rect Of(WallSegment wall) => new(wall.MinX, wall.MaxX, wall.MinZ, wall.MaxZ);

        public Rect Grown(float by) => new(MinX - by, MaxX + by, MinZ - by, MaxZ + by);

        /// <summary>Whether the two share any area.</summary>
        public bool Overlaps(Rect other) => MinX < other.MaxX && MaxX > other.MinX && MinZ < other.MaxZ && MaxZ > other.MinZ;
    }
}
