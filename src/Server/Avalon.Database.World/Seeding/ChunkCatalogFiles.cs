// The DTO set of the files under Maps/ is kept in one file, beside the catalog record that holds them.
#pragma warning disable MA0048

namespace Avalon.Database.World.Seeding;

/// <summary>
/// The chunk catalog under Maps/, read and checked file by file with no database (ChunkCatalogSeeder.ReadCatalogAsync):
/// what SeedAsync writes, and what tools/Avalon.ChunkGen validates its output with.
/// </summary>
public sealed record ChunkCatalogFiles(
    IReadOnlyList<ChunkMetaDto> Chunks,
    IReadOnlyList<(string Path, TownLayoutDto Layout)> Layouts,
    IReadOnlyDictionary<string, string[]> Pools,
    IReadOnlyDictionary<string, GroupDto[]>? Groups,
    IReadOnlyDictionary<string, SpawnTableEntryDto[]>? SpawnTables,
    IReadOnlyList<(string Path, ProceduralMapDto Map)> ProceduralMaps);

/// <summary>One entry of a spawn table in Maps/spawn-tables.json: a slot tag, a creature template, its weight and count range.</summary>
public sealed record SpawnTableEntryDto(string Tag, ulong CreatureId, float Weight, byte Min, byte Max);

/// <summary>A depth band in Maps/ProceduralMaps/&lt;mapId&gt;.json (see ProceduralDepthBand).</summary>
public sealed record DepthBandDto(int MinDepth, int? MaxDepth, ushort MinLevel, ushort MaxLevel);

/// <summary>One procedural map's config, Maps/ProceduralMaps/&lt;mapId&gt;.json; its pool and spawn table by name.</summary>
public sealed record ProceduralMapDto(
    ushort MapTemplateId, string ChunkPool, string SpawnTable, ushort MainPathMin, ushort MainPathMax, float BranchChance,
    byte BranchMaxDepth, bool HasBoss, ushort BackPortalTargetMapId, ushort? ForwardPortalTargetMapId,
    List<DepthBandDto>? DepthBands);

/// <summary>One set piece in Maps/chunk-groups.json.</summary>
public sealed record GroupDto(string Name, List<GroupMemberDto> Members);

/// <summary>A set piece's member chunk and its cell in the piece's own frame (0,0 = south-west).</summary>
public sealed record GroupMemberDto(string Chunk, int CellX, int CellZ);

/// <summary>One chunk's metadata file, Maps/Chunks/&lt;name&gt;.json.</summary>
public sealed record ChunkMetaDto(
    string Name,
    string AssetKey,
    byte CellFootprintX,
    byte CellFootprintZ,
    float CellSize,
    Dictionary<string, string[]> Exits,
    List<SpawnSlotDto> SpawnSlots,
    List<PortalSlotDto> PortalSlots,
    string[] Tags);

public sealed record SpawnSlotDto(string Tag, float LocalX, float LocalY, float LocalZ);

public sealed record PortalSlotDto(string Role, float LocalX, float LocalY, float LocalZ);

/// <summary>One town layout, Maps/TownLayouts/*.json.</summary>
public sealed record TownLayoutDto(int MapTemplateId, string MapName, float CellSize, List<TownChunkPlacementDto> Chunks);

public sealed record TownChunkPlacementDto(
    string ChunkName,
    short GridX,
    short GridZ,
    byte Rotation,
    bool IsEntry,
    EntrySpawnDto? EntrySpawn,
    ushort? BackPortalTargetMapId,
    ushort? ForwardPortalTargetMapId);

public sealed record EntrySpawnDto(float LocalX, float LocalY, float LocalZ);
