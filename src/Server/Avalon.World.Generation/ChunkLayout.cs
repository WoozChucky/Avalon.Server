using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.World.ChunkLayouts;

/// <summary>
/// One chunk of a layout. Depth is its distance in grid steps from the entry along stitched connections (0 for the
/// entry and for every predefined town chunk); Group names the set piece it belongs to, if any (forest content pass).
/// </summary>
public record PlacedChunk(
    ChunkTemplateId TemplateId,
    short GridX,
    short GridZ,
    byte Rotation,           // 0..3 × 90°
    Vector3 WorldPos,
    int Depth = 0,
    string? Group = null);

public record PortalPlacement(
    PortalRole Role,
    Vector3 WorldPos,
    ushort TargetMapId,
    float Radius = 1.5f);

/// <param name="Obstacles">
/// The solid doodads' boxes the navmesh bake cuts out and the layout packet carries, set by the instance build from its
/// doodad plan before the bake; null (none) for a layout no plan has touched.
/// </param>
public record ChunkLayout(
    int Seed,
    IReadOnlyList<PlacedChunk> Chunks,
    PlacedChunk EntryChunk,
    PlacedChunk? BossChunk,
    IReadOnlyList<PortalPlacement> Portals,
    Vector3 EntrySpawnWorldPos,
    float CellSize,
    ProceduralMapConfig? Config = null,
    string? ConfigVersion = null,
    int MainPathLength = 0,
    IReadOnlyList<DoodadObstacle>? Obstacles = null);

/// <summary>
/// A solid doodad's box as the navmesh bake cuts it out: its world X/Z bounds, the height it stands at (its slot's) and
/// its top. Planned with the doodads, before the bake (DoodadPlan.Obstacles); written into the bake by
/// DoodadObstacleGeometry.
/// </summary>
public readonly record struct DoodadObstacle(float MinX, float MaxX, float MinZ, float MaxZ, float BaseY, float TopY);
