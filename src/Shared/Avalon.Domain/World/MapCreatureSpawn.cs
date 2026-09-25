using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One creature, hand-placed at one spot on one map. The authored counterpart to the procedural
/// path, where a <see cref="SpawnTable"/> is rolled against a chunk's spawn slots: these rows say
/// exactly which creature stands exactly where, so the innkeeper is in the inn for every player,
/// every time.
/// </summary>
/// <remarks>
/// <para>
/// This is the only way a creature reaches a town. Procedural placement needs a
/// <see cref="ProceduralMapConfig"/> and chunk spawn slots, and a predefined town layout has
/// neither.
/// </para>
/// <para>
/// Positions are offsets in metres from the map's entry spawn point, not absolute world
/// coordinates. A town's world coordinates are a product of its <see cref="MapChunkPlacement"/>
/// rows and the layout cell size, so an absolute position seeded here would drift silently the
/// first time the town's chunks moved. <see cref="OffsetY"/> is a hint only — placement snaps the
/// final height to the navmesh.
/// </para>
/// </remarks>
public class MapCreatureSpawn : IDbEntity<MapCreatureSpawnId>
{
    public MapCreatureSpawnId Id { get; set; } = default!;

    public MapTemplateId MapTemplateId { get; set; } = default!;

    public CreatureTemplateId CreatureTemplateId { get; set; } = default!;

    public float OffsetX { get; set; }

    /// <summary>
    /// Vertical hint, used as the centre of the navmesh search box rather than as the final height.
    /// 0 is right for anything standing on the same floor as the entry point.
    /// </summary>
    public float OffsetY { get; set; }

    public float OffsetZ { get; set; }

    /// <summary>Yaw in degrees the creature faces once placed, matching <c>IUnit.Orientation.y</c>.</summary>
    public float Facing { get; set; }

    /// <summary>
    /// The route this creature walks, or <c>null</c> to stand where it was placed. The template's
    /// <c>ScriptName</c> still decides the AI: only a script that reads <c>ICreature.PatrolPath</c>,
    /// such as <c>CreaturePatrolScript</c>, walks it.
    /// </summary>
    public CreaturePathId? PathId { get; set; }

    public CreaturePath? Path { get; set; }
}
