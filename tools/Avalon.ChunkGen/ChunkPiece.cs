using Avalon.Database.World.Seeding;

namespace Avalon.ChunkGen;

public enum Side { N, E, S, W }

/// <summary>A spawn slot in chunk-local metres. Written with localY 1, as the forest's authored slots are; it only centres the ground search.</summary>
public sealed record Slot(string Tag, float X, float Z);

/// <summary>A solid standing on the floor, up to <see cref="ChunkPiece.BlockerHeight" />: it cuts the navmesh, and walkable rays stop at it.</summary>
public abstract record Blocker
{
    /// <summary>Metres from (x, z) to the blocker's footprint on X/Z; 0 inside it.</summary>
    public abstract float DistanceTo(float x, float z);

    public abstract (float MinX, float MaxX, float MinZ, float MaxZ) Bounds { get; }

    /// <summary>The same blocker moved by (dx, dz).</summary>
    public abstract Blocker Moved(float dx, float dz);
}

/// <summary>A rock or a ruined wall: an axis-aligned box.</summary>
public sealed record BoxBlocker(float MinX, float MaxX, float MinZ, float MaxZ) : Blocker
{
    public override float DistanceTo(float x, float z)
    {
        float dx = Math.Max(Math.Max(MinX - x, 0f), x - MaxX);
        float dz = Math.Max(Math.Max(MinZ - z, 0f), z - MaxZ);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public override (float MinX, float MaxX, float MinZ, float MaxZ) Bounds => (MinX, MaxX, MinZ, MaxZ);

    public override Blocker Moved(float dx, float dz) => new BoxBlocker(MinX + dx, MaxX + dx, MinZ + dz, MaxZ + dz);
}

/// <summary>A tree cluster or a pillar: a twelve-sided prism.</summary>
public sealed record CylinderBlocker(float X, float Z, float Radius) : Blocker
{
    public const int Segments = 12;

    public override float DistanceTo(float x, float z) =>
        Math.Max(0f, MathF.Sqrt((x - X) * (x - X) + (z - Z) * (z - Z)) - Radius);

    public override (float MinX, float MaxX, float MinZ, float MaxZ) Bounds => (X - Radius, X + Radius, Z - Radius, Z + Radius);

    public override Blocker Moved(float dx, float dz) => this with { X = X + dx, Z = Z + dz };
}

/// <summary>
/// One generated 1x1 chunk: a flat 30 x 30 m floor (the forest's slab, y -0.2 to 0), blockers on it, exits at the centre
/// of the named sides (logical only: forest floors have no walls), spawn slots and catalog tags.
/// </summary>
public sealed record ChunkPiece(
    string Name, IReadOnlyList<Side> Exits, IReadOnlyList<Blocker> Blockers, IReadOnlyList<Slot> Slots, IReadOnlyList<string> Tags)
{
    public const float CellSize = 30f;
    public const float BlockerHeight = 3f;
    public const float FloorThickness = 0.2f;

    /// <summary>
    /// Refuses a blocker or a slot that does not lie within the piece's 0-30 m cell (its edges included): the bake and the
    /// spawner would place it in a neighbouring chunk's ground.
    /// </summary>
    public void Validate()
    {
        foreach (Blocker blocker in Blockers)
        {
            (float minX, float maxX, float minZ, float maxZ) = blocker.Bounds;
            if (!WithinCell(minX) || !WithinCell(maxX) || !WithinCell(minZ) || !WithinCell(maxZ))
                throw new InvalidOperationException($"{Name}: blocker {blocker} lies outside the 0-30 m cell");
        }

        foreach (Slot slot in Slots)
        {
            if (!WithinCell(slot.X) || !WithinCell(slot.Z))
                throw new InvalidOperationException($"{Name}: slot {slot} lies outside the 0-30 m cell");
        }
    }

    /// <summary>NaN is outside.</summary>
    private static bool WithinCell(float v) => v >= 0f && v <= CellSize;

    /// <summary>The piece's catalog entry, as the seeder's own DTO: a 1x1 chunk, exits at the centre of the named sides.</summary>
    public ChunkMetaDto ToMeta() => new(
        Name,
        $"chunks/{Name}",
        1,
        1,
        CellSize,
        new Dictionary<string, string[]>
        {
            ["N"] = Exits.Contains(Side.N) ? ["center"] : [],
            ["E"] = Exits.Contains(Side.E) ? ["center"] : [],
            ["S"] = Exits.Contains(Side.S) ? ["center"] : [],
            ["W"] = Exits.Contains(Side.W) ? ["center"] : [],
        },
        Slots.Select(s => new SpawnSlotDto(s.Tag, s.X, 1f, s.Z)).ToList(),
        [],
        [.. Tags]);
}
