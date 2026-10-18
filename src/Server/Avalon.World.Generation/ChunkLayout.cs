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
    int MainPathLength = 0);

/// <summary>
/// A solid doodad's box as the navmesh bake cuts it out: its world X/Z bounds, the ground it stands on and its top. Planned
/// with the doodads, before the bake (DoodadPlan.Obstacles).
/// </summary>
public readonly record struct DoodadObstacle(float MinX, float MaxX, float MinZ, float MaxZ, float BaseY, float TopY);
