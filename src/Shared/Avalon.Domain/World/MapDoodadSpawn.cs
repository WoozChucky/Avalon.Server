using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One doodad, placed at one fixed spot on one map: the authored counterpart to a procedural map's doodad rules, as
/// <see cref="MapCreatureSpawn" /> is to its spawn tables. Glimmerdell's notice board is one.
/// </summary>
/// <remarks>
/// Positions are offsets in metres from the map's entry spawn point, as a <see cref="MapCreatureSpawn" />'s are, so a
/// town's doodads move with its chunks. A walk-through doodad's height is the navmesh's under it (the offset's Y only
/// centres the search); a solid one keeps the authored height and faces a quarter turn. Nothing is baked from a fixed
/// spawn, so v1 places no solid town doodad: one would need its box authored in ChunkGen. The notice board is
/// walk-through as a doodad; its collision box is a chunk proxy that ChunkGen keeps in the chunk's .obj, so it must
/// never be hidden. A gate set here replaces the template's for this doodad; each gate's quest and condition are both
/// set or both null.
/// </remarks>
public class MapDoodadSpawn : IDbEntity<MapDoodadSpawnId>
{
    public MapDoodadSpawnId Id { get; set; } = default!;

    public MapTemplateId MapTemplateId { get; set; } = default!;

    public DoodadTemplateId DoodadTemplateId { get; set; } = default!;

    public float OffsetX { get; set; }

    /// <summary>Vertical: a walk-through doodad's search-box centre, a solid one's height above the entry.</summary>
    public float OffsetY { get; set; }

    public float OffsetZ { get; set; }

    /// <summary>Yaw in degrees, 0 to 360: a quarter turn for a solid doodad.</summary>
    public float Yaw { get; set; }

    /// <summary>The spawn's own visibility gate quest; with VisibleCondition, it replaces the template's gate.</summary>
    public uint? VisibleQuestId { get; set; }

    /// <summary>Set exactly when VisibleQuestId is.</summary>
    public DoodadGateCondition? VisibleCondition { get; set; }

    /// <summary>The objective an OnObjective visibility gate names.</summary>
    public uint? VisibleObjectiveId { get; set; }

    /// <summary>The spawn's own usability gate quest; with UsableCondition, it replaces the template's gate.</summary>
    public uint? UsableQuestId { get; set; }

    /// <summary>Set exactly when UsableQuestId is.</summary>
    public DoodadGateCondition? UsableCondition { get; set; }

    /// <summary>The objective an OnObjective usability gate names.</summary>
    public uint? UsableObjectiveId { get; set; }
}
