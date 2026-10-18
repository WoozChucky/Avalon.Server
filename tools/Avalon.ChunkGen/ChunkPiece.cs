using Avalon.Database.World.Seeding;

namespace Avalon.ChunkGen;

public enum Side { N, E, S, W }

/// <summary>A spawn slot in chunk-local metres. Written with localY 1, as the forest's authored slots are; it only centres the ground search.</summary>
public sealed record Slot(string Tag, float X, float Z);

/// <summary>
/// A doodad slot in chunk-local metres, its yaw a quarter turn in degrees. Written with localY 0 (ChunkPiece.DoodadSlotY),
/// on the floor: a solid doodad keeps its slot's height, so its box and model stand on the ground.
/// Tags: <c>doodad</c> (walk-through doodads) and <c>doodad_large</c> (may hold a solid one).
/// </summary>
public sealed record DoodadSlot(string Tag, float X, float Z, int Yaw);

/// <summary>
/// What the forest's decor draws on a blocker (ForestDresser), so the client does not draw the blocker itself
/// (ObjWriter writes a dressed one <c>usemtl collision</c>). <see cref="None" /> is an undressed placeholder.
/// </summary>
public enum Dressing
{
    /// <summary>Not dressed: the client draws it as the placeholder it is.</summary>
    None,

    /// <summary>A tree cluster's drum: two to four trees standing inside it near its rim, bushes and rocks filling it.</summary>
    TreeDrum,

    /// <summary>Rocks: a custom rock and kit rocks scaled to fit, or the custom rock of <see cref="Blocker.Prop" />.</summary>
    Rock,

    /// <summary>A ruined wall: the custom ruin wall sections along its length, pillar stumps and rubble beside them.</summary>
    RuinWall,

    /// <summary>A pillar: a custom ruin pillar and a stump, with rubble.</summary>
    Pillar,

    /// <summary>A fallen log: <see cref="Blocker.Prop" />'s footprint.</summary>
    Log,

    /// <summary>A free-standing tree's trunk: the tree of <see cref="Blocker.Tree" /> at the cylinder's centre.</summary>
    Trunk,

    /// <summary>A leg of a walk-under rock arch: a part of <see cref="Blocker.Prop" />'s footprint.</summary>
    ArchLeg,

    /// <summary>A leaning rock: a part of <see cref="Blocker.Prop" />'s footprint.</summary>
    LeaningRock,

    /// <summary>The base of a rock overhang: a part of <see cref="Blocker.Prop" />'s footprint.</summary>
    Overhang,
}

/// <summary>The kit's tree species; the variants of one share a trunk radius (ForestDresser.TrunkRadius).</summary>
public enum TreeSpecies { CommonTree, Pine, DeadTree, TwistedTree }

/// <summary>A custom forest model (Kit/forest-footprints.json) whose footprint a blocker is a part of, placed at scale 1.</summary>
public sealed record PropPlacement(string Model, float X, float Z, int Yaw);

/// <summary>The tree a <see cref="Dressing.Trunk" /> blocker stands for: its species and scale; the dresser picks its variant and yaw.</summary>
public sealed record TreePlacement(TreeSpecies Species, float Scale);

/// <summary>A solid standing on the floor, up to <see cref="ChunkPiece.BlockerHeight" />: it cuts the navmesh, and walkable rays stop at it.</summary>
public abstract record Blocker
{
    /// <summary>What the forest's decor draws on it; <see cref="Dressing.None" /> leaves it a placeholder.</summary>
    public Dressing Dressing { get; init; }

    /// <summary>The custom model whose footprint part this is, when one model alone draws it (a log, a leaning rock, an arch leg).</summary>
    public PropPlacement? Prop { get; init; }

    /// <summary>The tree a <see cref="Dressing.Trunk" /> stands for.</summary>
    public TreePlacement? Tree { get; init; }

    /// <summary>Metres from (x, z) to the blocker's footprint on X/Z; 0 inside it.</summary>
    public abstract float DistanceTo(float x, float z);

    public abstract (float MinX, float MaxX, float MinZ, float MaxZ) Bounds { get; }

    /// <summary>The same blocker, and the prop it is a part of, moved by (dx, dz).</summary>
    public abstract Blocker Moved(float dx, float dz);

    private protected PropPlacement? PropMoved(float dx, float dz) => Prop is null ? null : Prop with { X = Prop.X + dx, Z = Prop.Z + dz };
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

    public override Blocker Moved(float dx, float dz) =>
        this with { MinX = MinX + dx, MaxX = MaxX + dx, MinZ = MinZ + dz, MaxZ = MaxZ + dz, Prop = PropMoved(dx, dz) };
}

/// <summary>A tree cluster, a trunk or a pillar: a twelve-sided prism, its corners at <paramref name="Radius" />, the first on +X.</summary>
public sealed record CylinderBlocker(float X, float Z, float Radius) : Blocker
{
    public const int Segments = 12;

    /// <summary>cos 15°: how far a flat side stands from the centre, per metre of radius.</summary>
    public const float Apothem = 0.9659258f;

    public override float DistanceTo(float x, float z) =>
        Math.Max(0f, MathF.Sqrt((x - X) * (x - X) + (z - Z) * (z - Z)) - Radius);

    public override (float MinX, float MaxX, float MinZ, float MaxZ) Bounds => (X - Radius, X + Radius, Z - Radius, Z + Radius);

    public override Blocker Moved(float dx, float dz) => this with { X = X + dx, Z = Z + dz, Prop = PropMoved(dx, dz) };
}

/// <summary>Which species the forest dresser draws a piece's trees from (ForestDresser).</summary>
public enum TreeMix
{
    /// <summary>Mostly broadleaf trees, a third pines, a few dead ones.</summary>
    Mixed,

    /// <summary>Mostly broadleaf: a lighter wood.</summary>
    Broadleaf,

    /// <summary>Mostly pines: a darker wood.</summary>
    Pine,

    /// <summary>An old wood: dead trees among the broadleaf and the pines, and now and then a twisted one.</summary>
    Old,
}

/// <summary>
/// How the forest dresser dresses a piece beyond its blockers: the tree mix it draws from, and the meeting point its
/// paths run to (the centre of the piece, or of its set piece's frame, unless set).
/// </summary>
public sealed record DressingHints(TreeMix Trees = TreeMix.Mixed, float? MeetX = null, float? MeetZ = null);

/// <summary>
/// One generated 1x1 chunk: a flat 30 x 30 m floor (the forest's slab, y -0.2 to 0), blockers on it, exits at the centre
/// of the named sides (logical only: forest floors have no walls), spawn slots, doodad slots and catalog tags.
/// </summary>
public sealed record ChunkPiece(
    string Name, IReadOnlyList<Side> Exits, IReadOnlyList<Blocker> Blockers, IReadOnlyList<Slot> Slots, IReadOnlyList<string> Tags)
{
    /// <summary>The piece's doodad slots, none unless listed.</summary>
    public IReadOnlyList<DoodadSlot> DoodadSlots { get; init; } = [];

    /// <summary>How the forest dresser dresses it; a set piece's member is dressed with its set piece, in its frame.</summary>
    public DressingHints Hints { get; init; } = new();

    public const float CellSize = 30f;
    public const float BlockerHeight = 3f;
    public const float FloorThickness = 0.2f;

    /// <summary>The height a doodad slot is written at: the floor's top (ObjWriter). A spawn slot is written 1 m up.</summary>
    public const float DoodadSlotY = 0f;

    /// <summary>
    /// Refuses a blocker or a slot that does not lie within the piece's 0-30 m cell (its edges included): the bake and the
    /// spawner would place it in a neighbouring chunk's ground. A doodad slot also needs a tag and a quarter-turn yaw.
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

        foreach (DoodadSlot slot in DoodadSlots)
        {
            if (!WithinCell(slot.X) || !WithinCell(slot.Z))
                throw new InvalidOperationException($"{Name}: doodad slot {slot} lies outside the 0-30 m cell");
            if (string.IsNullOrWhiteSpace(slot.Tag) || slot.Yaw is not (0 or 90 or 180 or 270))
                throw new InvalidOperationException($"{Name}: doodad slot {slot} needs a tag and a yaw of 0, 90, 180 or 270");
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
        [.. Tags],
        DoodadSlots.Select(s => new DoodadSlotDto(s.Tag, s.X, DoodadSlotY, s.Z, s.Yaw)).ToList());
}
