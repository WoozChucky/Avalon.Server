using System.Text;
using Avalon.Database.World.Seeding;

namespace Avalon.ChunkGen;

/// <summary>The named materials a town shape is tagged with ("usemtl"); the client colours them, the server ignores them.</summary>
public enum Material { Stone, Wood, Roof, Cloth, Cloth2, Plaster, Metal, Water }

public static class Materials
{
    public static string Name(Material material) => material switch
    {
        Material.Stone => "stone",
        Material.Wood => "wood",
        Material.Roof => "roof",
        Material.Cloth => "cloth",
        Material.Cloth2 => "cloth_2",
        Material.Plaster => "plaster",
        Material.Metal => "metal",
        Material.Water => "water",
        _ => throw new ArgumentOutOfRangeException(nameof(material), material, "unknown material"),
    };
}

/// <summary>The side of a building its open face is on: a porch, a counter, a lean-to, the steps.</summary>
public enum Facing { None, NegX, PosX, NegZ, PosZ }

/// <summary>
/// One solid shape of a town square, in chunk-local metres: part of a building or a prop. A walkable piece (a porch deck,
/// a step) is a riser the navmesh climbs; everything else blocks.
/// </summary>
public abstract record TownPiece(string Building, string Part, Material Material, float Y0, float Top, bool Walkable)
{
    /// <summary>
    /// Set on the one piece that is a building's open face, and only there. The client's camera is fixed at yaw 45,
    /// south-west of what it looks at, so a front on the -X or -Z side is the one a player sees.
    /// </summary>
    public Facing Front { get; init; } = Facing.None;

    /// <summary>The footprint's axis-aligned bounds on X/Z.</summary>
    public abstract (float MinX, float MaxX, float MinZ, float MaxZ) Bounds { get; }

    /// <summary>Metres from (x, z) to the footprint; 0 inside it.</summary>
    public abstract float DistanceTo(float x, float z);

    public abstract (float X, float Z) Centre { get; }

    public string ObjectName => TownSquare.ObjectName(Building, Part);
}

/// <summary>A box: a building body, a counter, a crate, a bench, a deck, a step, a lintel, a flat roof slab.</summary>
public sealed record BoxPiece(
    string Building, string Part, Material Material, float MinX, float MaxX, float MinZ, float MaxZ, float Y0, float Top,
    bool Walkable = false) : TownPiece(Building, Part, Material, Y0, Top, Walkable)
{
    public override (float MinX, float MaxX, float MinZ, float MaxZ) Bounds => (MinX, MaxX, MinZ, MaxZ);

    public override float DistanceTo(float x, float z)
    {
        float dx = Math.Max(Math.Max(MinX - x, 0f), x - MaxX);
        float dz = Math.Max(Math.Max(MinZ - z, 0f), z - MaxZ);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public override (float X, float Z) Centre => ((MinX + MaxX) / 2f, (MinZ + MaxZ) / 2f);
}

/// <summary>A twelve-sided prism: a post, a lamp post, a barrel, a chimney, the fountain's parts, the well ring.</summary>
public sealed record CylinderPiece(string Building, string Part, Material Material, float X, float Z, float Radius, float Y0, float Top)
    : TownPiece(Building, Part, Material, Y0, Top, Walkable: false)
{
    public override (float MinX, float MaxX, float MinZ, float MaxZ) Bounds => (X - Radius, X + Radius, Z - Radius, Z + Radius);

    public override float DistanceTo(float x, float z) =>
        Math.Max(0f, MathF.Sqrt((x - X) * (x - X) + (z - Z) * (z - Z)) - Radius);

    public override (float X, float Z) Centre => (X, Z);
}

/// <summary>
/// A twelve-sided ring: a hollow rim standing on the floor, the fountain's basin, with its hole open to the ground (or
/// to a water disc) inside. Solid between <paramref name="InnerRadius" /> and <paramref name="Radius" />.
/// </summary>
public sealed record RingPiece(string Building, string Part, Material Material, float X, float Z, float Radius, float InnerRadius, float Y0, float Top)
    : TownPiece(Building, Part, Material, Y0, Top, Walkable: false)
{
    public override (float MinX, float MaxX, float MinZ, float MaxZ) Bounds => (X - Radius, X + Radius, Z - Radius, Z + Radius);

    /// <summary>Distance to the outer circle: the hole is not open ground to anything outside the ring.</summary>
    public override float DistanceTo(float x, float z) =>
        Math.Max(0f, MathF.Sqrt((x - X) * (x - X) + (z - Z) * (z - Z)) - Radius);

    public override (float X, float Z) Centre => (X, Z);

    /// <summary>Whether a piece's footprint lies wholly inside the hole and no higher than the rim (the water disc of a basin).</summary>
    public bool Encloses(TownPiece piece)
    {
        float reach;
        if (piece is CylinderPiece cylinder)
        {
            reach = MathF.Sqrt((cylinder.X - X) * (cylinder.X - X) + (cylinder.Z - Z) * (cylinder.Z - Z)) + cylinder.Radius;
        }
        else
        {
            (float minX, float maxX, float minZ, float maxZ) = piece.Bounds;
            reach = 0f;
            foreach ((float cx, float cz) in new[] { (minX, minZ), (maxX, minZ), (minX, maxZ), (maxX, maxZ) })
                reach = Math.Max(reach, MathF.Sqrt((cx - X) * (cx - X) + (cz - Z) * (cz - Z)));
        }
        return reach <= InnerRadius && piece.Top <= Top;
    }
}

/// <summary>
/// A sloped roof block: a prism whose eaves lie on the footprint at <paramref name="Eaves" /> and whose ridge runs the
/// full length of the longer footprint axis (X when square) at <paramref name="Ridge" />, with triangular ends.
/// </summary>
public sealed record GablePiece(
    string Building, string Part, Material Material, float MinX, float MaxX, float MinZ, float MaxZ, float Eaves, float Ridge)
    : TownPiece(Building, Part, Material, Eaves, Ridge, Walkable: false)
{
    public bool RidgeAlongX => MaxX - MinX >= MaxZ - MinZ;

    public override (float MinX, float MaxX, float MinZ, float MaxZ) Bounds => (MinX, MaxX, MinZ, MaxZ);

    public override float DistanceTo(float x, float z)
    {
        float dx = Math.Max(Math.Max(MinX - x, 0f), x - MaxX);
        float dz = Math.Max(Math.Max(MinZ - z, 0f), z - MaxZ);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public override (float X, float Z) Centre => ((MinX + MaxX) / 2f, (MinZ + MaxZ) / 2f);
}

/// <summary>One of the town's wall boxes, 0 to <see cref="Height" /> m, named by its side (Wall_N_L, ...).</summary>
public sealed record WallSegment(string Name, float MinX, float MaxX, float MinZ, float MaxZ)
{
    public const float Height = 2f;

    public float DistanceTo(float x, float z)
    {
        float dx = Math.Max(Math.Max(MinX - x, 0f), x - MaxX);
        float dz = Math.Max(Math.Max(MinZ - z, 0f), z - MaxZ);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Metres from a piece's footprint to this wall on X/Z; 0 when they overlap.</summary>
    public float DistanceTo(TownPiece piece)
    {
        if (piece is CylinderPiece cylinder)
            return Math.Max(0f, DistanceTo(cylinder.X, cylinder.Z) - cylinder.Radius);
        if (piece is RingPiece ring)
            return Math.Max(0f, DistanceTo(ring.X, ring.Z) - ring.Radius);
        (float minX, float maxX, float minZ, float maxZ) = piece.Bounds;
        float dx = Math.Max(Math.Max(MinX - maxX, 0f), minX - MaxX);
        float dz = Math.Max(Math.Max(MinZ - maxZ, 0f), minZ - MaxZ);
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}

/// <summary>
/// One 30 x 30 m town square: today's floor (y -0.05 to 0.05) and wall boxes, reproduced exactly, plus the buildings and
/// props the approved layout puts on it. A 1x1 chunk with exits at the centre of the named sides; the entry square
/// carries the "entry" spawn slot at (15, 0, 15) and the portal square the Forward portal slot at (15, 0, 15), as the
/// hand-authored files did.
/// </summary>
public sealed record TownSquare(
    string Name, int GridX, int GridZ, IReadOnlyList<Side> Exits, IReadOnlyList<WallSegment> Walls,
    IReadOnlyList<TownPiece> Pieces, bool IsEntry, bool HasForwardPortal, IReadOnlyList<string> Tags)
{
    public const float CellSize = ChunkPiece.CellSize;
    public const float FloorBottom = -0.05f;
    public const float FloorTop = 0.05f;

    /// <summary>The square's south-west corner in world metres.</summary>
    public (float X, float Z) Origin => (GridX * CellSize, GridZ * CellSize);

    /// <summary>
    /// Refuses what the bake could not place: a piece outside the 0-30 m cell (a roof's overhang included), a bound that
    /// is not a number, a top not above its base, a wall not named "Wall...", a piece named like a wall (the placement
    /// tests read wall boxes by that prefix), and then every rule in <see cref="TownRules" />.
    /// </summary>
    public void Validate()
    {
        foreach (WallSegment wall in Walls)
        {
            if (!wall.Name.StartsWith("Wall", StringComparison.Ordinal))
                throw new InvalidOperationException($"{Name}: wall '{wall.Name}' must be named Wall...");
        }

        foreach (TownPiece piece in Pieces)
        {
            (float minX, float maxX, float minZ, float maxZ) = piece.Bounds;
            if (!WithinCell(minX) || !WithinCell(maxX) || !WithinCell(minZ) || !WithinCell(maxZ))
                throw new InvalidOperationException($"{Name}: {piece.Building}/{piece.Part} lies outside the 0-30 m cell");
            if (!float.IsFinite(piece.Y0) || !float.IsFinite(piece.Top) || piece.Top <= piece.Y0)
                throw new InvalidOperationException($"{Name}: {piece.Building}/{piece.Part} has no height ({piece.Y0} to {piece.Top})");
            if (piece is CylinderPiece { Radius: <= 0f })
                throw new InvalidOperationException($"{Name}: {piece.Building}/{piece.Part} has no radius");
            if (piece is RingPiece ring && (ring.InnerRadius <= 0f || ring.InnerRadius >= ring.Radius))
                throw new InvalidOperationException($"{Name}: {piece.Building}/{piece.Part} has no rim ({ring.InnerRadius} inside {ring.Radius})");
            if (piece.ObjectName.StartsWith("Wall", StringComparison.Ordinal))
                throw new InvalidOperationException($"{Name}: {piece.Building}/{piece.Part} would be named like a wall ({piece.ObjectName})");
        }

        TownRules.Check(this);
    }

    /// <summary>NaN is outside.</summary>
    private static bool WithinCell(float v) => v >= 0f && v <= CellSize;

    public ChunkMetaDto ToMeta() => new(
        Name,
        $"chunks/{Name}",
        1,
        1,
        CellSize,
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["N"] = Exits.Contains(Side.N) ? ["center"] : [],
            ["E"] = Exits.Contains(Side.E) ? ["center"] : [],
            ["S"] = Exits.Contains(Side.S) ? ["center"] : [],
            ["W"] = Exits.Contains(Side.W) ? ["center"] : [],
        },
        IsEntry ? [new SpawnSlotDto("entry", CellSize / 2f, 0f, CellSize / 2f)] : [],
        HasForwardPortal ? [new PortalSlotDto("Forward", CellSize / 2f, 0f, CellSize / 2f)] : [],
        [.. Tags]);

    /// <summary>
    /// The obj object name of a piece: the building in PascalCase (letters and digits only) and the part in lower snake
    /// case, joined by "_": "Town hall" / "porch deck" is TownHall_porch_deck.
    /// </summary>
    public static string ObjectName(string building, string part)
    {
        var pascal = new StringBuilder();
        bool startWord = true;
        foreach (char c in building)
        {
            if (c == '\'') continue;   // "Hunter's lodge" is HuntersLodge, not a new word
            if (!char.IsAsciiLetterOrDigit(c)) { startWord = true; continue; }
            pascal.Append(startWord ? char.ToUpperInvariant(c) : c);
            startWord = false;
        }

        var snake = new StringBuilder();
        foreach (char c in part)
        {
            if (char.IsAsciiLetterOrDigit(c)) snake.Append(char.ToLowerInvariant(c));
            else if (snake.Length > 0 && snake[^1] != '_') snake.Append('_');
        }
        while (snake.Length > 0 && snake[^1] == '_') snake.Length--;

        return $"{pascal}_{snake}";
    }
}

/// <summary>
/// The owner's layout rules (spec of 2026-10-01): nothing within 2 m of a wall, roofs included; the doorway lanes clear
/// (6 m wide, 3 m deep each side of an opening); the arrival-to-portal corridor (world X 13-17, Z 15-45) clear below
/// 2.5 m; at least 2.5 m of headroom under any roof a unit stands under; a walkable riser of at most 0.3 m. Each is
/// checked here, chunk-local, so the tool refuses a square before it bakes; the NPC rules need the seeded rows and live
/// in the tests.
/// </summary>
public static class TownRules
{
    public const float WallClearance = 2f;
    public const float DoorwayLaneDepth = 3f;
    public const float DoorwayFrom = 12f;
    public const float DoorwayTo = 18f;
    public const float MinHeadroom = 2.5f;
    public const float MaxRiser = 0.3f;
    public const float CorridorMinX = 13f;
    public const float CorridorMaxX = 17f;
    public const float CorridorMinZ = 15f;
    public const float CorridorMaxZ = 45f;
    public const float CorridorClearHeight = 2.5f;

    /// <summary>An NPC stands at least this far from any solid below <see cref="SolidBelow" />.</summary>
    public const float NpcClearance = 1f;

    /// <summary>... and within this distance of a solid of its own building.</summary>
    public const float NpcBuildingReach = 3f;

    /// <summary>A piece whose base is at or above this is a roof, never something to stand against.</summary>
    public const float SolidBelow = 2f;

    /// <summary>The lane an exit on <paramref name="side" /> keeps clear, chunk-local.</summary>
    public static (float MinX, float MaxX, float MinZ, float MaxZ) LaneOf(Side side) => side switch
    {
        Side.N => (DoorwayFrom, DoorwayTo, TownSquare.CellSize - DoorwayLaneDepth, TownSquare.CellSize),
        Side.S => (DoorwayFrom, DoorwayTo, 0f, DoorwayLaneDepth),
        Side.E => (TownSquare.CellSize - DoorwayLaneDepth, TownSquare.CellSize, DoorwayFrom, DoorwayTo),
        _ => (0f, DoorwayLaneDepth, DoorwayFrom, DoorwayTo),
    };

    public static void Check(TownSquare square)
    {
        (float ox, float oz) = square.Origin;
        foreach (TownPiece piece in square.Pieces)
        {
            string what = $"{square.Name}: {piece.Building}/{piece.Part}";

            foreach (WallSegment wall in square.Walls)
            {
                float d = wall.DistanceTo(piece);
                if (d < WallClearance - 1e-6f)
                    throw new InvalidOperationException($"{what} is {d:0.00} m from {wall.Name} (under {WallClearance} m)");
            }

            foreach (Side exit in square.Exits)
            {
                if (Overlaps(piece, LaneOf(exit)))
                    throw new InvalidOperationException($"{what} stands in the {exit} doorway lane");
            }

            var corridor = (CorridorMinX - ox, CorridorMaxX - ox, CorridorMinZ - oz, CorridorMaxZ - oz);
            if (piece.Y0 < CorridorClearHeight && Overlaps(piece, corridor))
                throw new InvalidOperationException($"{what} stands in the arrival-to-portal corridor below {CorridorClearHeight} m");

            // A roof a unit stands under: a slab whose base is above SolidBelow (a porch roof, a stall roof, an
            // awning, the lean-to, the well roof, the lintel). A building's own roof block sits on its body, a
            // stacked crate (base 1 m) and a cart bed (base 0.5 m) are solids, so none of those is judged here.
            bool roofOverGround = !piece.Walkable && piece is not GablePiece && piece.Y0 >= SolidBelow;
            if (roofOverGround && piece.Y0 < MinHeadroom)
                throw new InvalidOperationException($"{what} leaves {piece.Y0:0.00} m of headroom (under {MinHeadroom} m)");

            if (piece.Walkable && piece.Top > MaxRiser + 1e-6f)
                throw new InvalidOperationException($"{what} is a riser of {piece.Top:0.00} m (over {MaxRiser} m)");
        }
    }

    /// <summary>Whether a piece's footprint and an axis-aligned rectangle share any area (a cylinder by its circle).</summary>
    public static bool Overlaps(TownPiece piece, (float MinX, float MaxX, float MinZ, float MaxZ) rect)
    {
        (float cx, float cz, float r) = piece switch
        {
            CylinderPiece cylinder => (cylinder.X, cylinder.Z, cylinder.Radius),
            RingPiece ring => (ring.X, ring.Z, ring.Radius),
            _ => (0f, 0f, 0f),
        };
        if (r > 0f)
        {
            float dx = Math.Max(Math.Max(rect.MinX - cx, 0f), cx - rect.MaxX);
            float dz = Math.Max(Math.Max(rect.MinZ - cz, 0f), cz - rect.MaxZ);
            return dx * dx + dz * dz < r * r;
        }

        (float minX, float maxX, float minZ, float maxZ) = piece.Bounds;
        return minX < rect.MaxX && maxX > rect.MinX && minZ < rect.MaxZ && maxZ > rect.MinZ;
    }
}
