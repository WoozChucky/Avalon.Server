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
    float Radius = 3.0f);

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
