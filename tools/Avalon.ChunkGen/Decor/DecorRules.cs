namespace Avalon.ChunkGen;

/// <summary>
/// What a player sees and what they collide with agree (spec of 2026-10-17). Every solid decor item's footprint, its
/// model's box from the bounds table turned by its yaw, lies within <see cref="Tolerance" /> of its building's
/// non-walkable proxies (a <see cref="Walls" /> item: of the square's wall boxes), so nothing drawn stands where a
/// player can walk. And no proxy of a building with solid decor, or of one drawn as <see cref="Material.Collision" />,
/// reaches more than <see cref="Tolerance" /> past that decor on its -X or -Z side, the side the fixed camera sees, so
/// no invisible wall shows. Flat ground tiles and thin hanging dressing (<see cref="DressingPrefixes" />) are not solid
/// and are not checked.
/// </summary>
public static class DecorRules
{
    public const float Tolerance = 0.15f;

    /// <summary>The building a square's own wall sections are placed under: they are checked against its wall boxes.</summary>
    public const string Walls = "Walls";

    /// <summary>Model name prefixes of what is flat on the ground or hangs thin on a wall, never collided with.</summary>
    public static readonly string[] DressingPrefixes = ["Prop_Vine", "WindowShutters_", "Window_", "Floor_", "Prop_ExteriorBorder_", "HoleCover_"];

    private static readonly Lazy<IReadOnlyDictionary<string, ModelBounds>> s_kit = new(KitBounds.Load, isThreadSafe: true);

    /// <summary>
    /// The item's model box turned by its yaw about the model's origin (as <see cref="TownBuilding.Place" /> turns a
    /// recipe) and moved to the item's position: chunk-local metres. A model the bounds table does not have, or a yaw
    /// that is not a quarter turn, is refused (InvalidOperationException).
    /// </summary>
    public static (float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ) WorldBounds(DecorItem item)
    {
        if (!s_kit.Value.TryGetValue(item.Model, out ModelBounds? b))
            throw new InvalidOperationException($"the kit's bounds table has no model '{item.Model}'");
        if (item.Yaw is not (0 or 90 or 180 or 270))
            throw new InvalidOperationException($"{item.Model} has yaw {item.Yaw}, not 0, 90, 180 or 270");

        (float minX, float maxX, float minZ, float maxZ) = TownBuilding.Footprint(b.MinX, b.MaxX, b.MinZ, b.MaxZ, item.X, item.Z, item.Yaw / 90);
        return (minX, maxX, item.Y + b.MinY, item.Y + b.MaxY, minZ, maxZ);
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

    /// <summary>
    /// Refuses (InvalidOperationException naming the square, the building and the model or part) a solid item outside
    /// its proxies or walls, a solid item of a building with no non-walkable proxy, and a proxy reaching past its decor
    /// on the camera's side, including a collision proxy whose building has no solid decor at all.
    /// </summary>
    public static void Check(TownSquare square)
    {
        var decorOf = new Dictionary<string, List<Rect>>(StringComparer.Ordinal);
        foreach (DecorItem item in square.Decor)
        {
            if (!IsSolid(item)) continue;
            (float minX, float maxX, _, _, float minZ, float maxZ) = WorldBounds(item);
            var footprint = new Rect(minX, maxX, minZ, maxZ);
            string what = $"{square.Name}: {item.Building}: {item.Model} at ({item.X}, {item.Z})";

            List<Rect> proxies = item.Building == Walls
                ? [.. square.Walls.Select(w => new Rect(w.MinX, w.MaxX, w.MinZ, w.MaxZ).Grown(Tolerance))]
                : [.. Solids(square, item.Building).Select(p => Rect.Of(p).Grown(Tolerance))];
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

            if (item.Building == Walls) continue;
            if (!decorOf.TryGetValue(item.Building, out List<Rect>? drawn))
                decorOf[item.Building] = drawn = [];
            drawn.Add(footprint);
        }

        foreach (string building in square.Pieces.Select(p => p.Building).Distinct(StringComparer.Ordinal))
        {
            bool drawnAsCollision = Solids(square, building).Any(p => p.Material == Material.Collision);
            if (!decorOf.TryGetValue(building, out List<Rect>? drawn))
            {
                if (drawnAsCollision)
                {
                    TownPiece invisible = Solids(square, building).First(p => p.Material == Material.Collision);
                    throw new InvalidOperationException(
                        $"{square.Name}: {building}/{invisible.Part} is collision, which the client does not draw, but {building} has no solid decor: an invisible wall");
                }
                continue;
            }

            float decorMinX = drawn.Min(r => r.MinX), decorMinZ = drawn.Min(r => r.MinZ);
            foreach (TownPiece piece in Solids(square, building))
            {
                (float minX, _, float minZ, _) = piece.Bounds;
                if (minX < decorMinX - Tolerance)
                    throw new InvalidOperationException(PastDecor(square, piece, decorMinX - minX, "-X"));
                if (minZ < decorMinZ - Tolerance)
                    throw new InvalidOperationException(PastDecor(square, piece, decorMinZ - minZ, "-Z"));
            }
        }
    }

    private static string PastDecor(TownSquare square, TownPiece piece, float by, string side) =>
        $"{square.Name}: {piece.Building}/{piece.Part} reaches {by:0.00} m past {piece.Building}'s decor on its {side} side " +
        $"(over {Tolerance} m): an invisible wall on the camera's side";

    /// <summary>A building's pieces a player collides with: everything but its walkable decks and steps.</summary>
    private static IEnumerable<TownPiece> Solids(TownSquare square, string building) =>
        square.Pieces.Where(p => !p.Walkable && p.Building == building);

    /// <summary>
    /// Whether the boxes cover the area. The boxes' edges cut the area into cells no edge crosses, so each box holds a
    /// whole cell or none of it, and testing each cell's centre is exact.
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

    private readonly record struct Rect(float MinX, float MaxX, float MinZ, float MaxZ)
    {
        public static Rect Of(TownPiece piece)
        {
            (float minX, float maxX, float minZ, float maxZ) = piece.Bounds;
            return new Rect(minX, maxX, minZ, maxZ);
        }

        public Rect Grown(float by) => new(MinX - by, MaxX + by, MinZ - by, MaxZ + by);
    }
}
