namespace Avalon.World.Reload;

/// <summary>
/// The reloadable areas. Maps and chunk layouts are deliberately absent: live instances have baked
/// a navmesh from them, and the client mirrors that bake.
/// </summary>
public enum ReloadArea { Dialogue, Creatures, Abilities, Items, Progression }
