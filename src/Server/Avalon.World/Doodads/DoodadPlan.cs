// A plan's records are kept together, beside the plan that holds them.
#pragma warning disable MA0048

using Avalon.Common.Mathematics;
using Avalon.World.ChunkLayouts;

namespace Avalon.World.Doodads;

/// <summary>
/// A doodad to place, before it has a spot: its template, whether its map must have it, and the spawn's own gates (a
/// rule's), which replace the template's when set, and the level range its drops roll their item level in (#1179): its
/// depth band's, null for a fixed spawn and one placed off a slot, whose drops take the map's range. What a required rule
/// is short of, at the fallback.
/// </summary>
public sealed record DoodadSpawn(DoodadTemplateView Template, bool Required = false, DoodadGate? VisibleGate = null,
    DoodadGate? UsableGate = null, LevelRange? Levels = null);

/// <summary>
/// A doodad the plan put at a chunk doodad slot: where it stands (the slot's world position, its height the slot's
/// until the placement snaps a walk-through one to the ground) and the yaw it faces. Only a slot is planned: the
/// fallback's spots on open navmesh are found after the bake, and never become one of these. <paramref name="Levels" /> is
/// the slot's depth band (#1179), null where the map has none there.
/// </summary>
public sealed record PlannedDoodad(DoodadTemplateView Template, Vector3 Position, float YawDegrees,
    bool Required = false, DoodadGate? VisibleGate = null, DoodadGate? UsableGate = null, LevelRange? Levels = null)
{
    /// <summary>The same doodad without its spot, for the fallback; its drops still roll in its slot's band.</summary>
    public DoodadSpawn Spawn => new(Template, Required, VisibleGate, UsableGate, Levels);
}

/// <summary>
/// What an instance build places of its procedural doodads, decided before the navmesh bake from the layout, its map's
/// rules and the catalog alone, so the same seed always gives the same plan, in every process (each chunk's slots are
/// sorted, never taken in the order the database loaded them): the doodads at slots, in the order they are placed, the
/// required ones no slot was left for (placed on open navmesh once the mesh is baked), and the solid doodads' boxes for
/// the bake to cut out, in the order of the doodads they belong to (the instance build sets them on the layout, which
/// the bake and the layout packet read).
/// </summary>
public sealed record DoodadPlan(IReadOnlyList<PlannedDoodad> Placed, IReadOnlyList<DoodadSpawn> RequiredShort,
    IReadOnlyList<DoodadObstacle> Obstacles)
{
    /// <summary>Nothing to place: a town, or a map without doodad rules.</summary>
    public static DoodadPlan Empty { get; } = new([], [], []);
}
