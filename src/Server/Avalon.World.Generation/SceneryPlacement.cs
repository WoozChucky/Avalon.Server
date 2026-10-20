namespace Avalon.World.ChunkLayouts;

/// <summary>
/// One scenery piece drawn in a ring cell around a map: the piece's name (its <c>Maps/Scenery/&lt;name&gt;.obj</c> and
/// <c>Maps/Decor/&lt;name&gt;.json</c>), the grid cell it fills and its rotation in quarter turns (0-3, as a chunk's).
/// Scenery is never a chunk: no catalog row, no exits, no navmesh, never walkable.
/// </summary>
public sealed record SceneryPlacement(string Name, int GridX, int GridZ, int Rotation);
