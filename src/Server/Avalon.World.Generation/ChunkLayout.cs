using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.World.ChunkLayouts;

public record PlacedChunk(
    ChunkTemplateId TemplateId,
    short GridX,
    short GridZ,
    byte Rotation,           // 0..3 × 90°
    Vector3 WorldPos);

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
    ProceduralMapConfig? Config = null);
