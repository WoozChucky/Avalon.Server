// The DTO set of the files under Maps/ is kept in one file, beside the catalog record that holds them.
#pragma warning disable MA0048

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avalon.Database.World.Seeding;

/// <summary>
/// The chunk catalog under Maps/, read and checked file by file with no database (ChunkCatalogSeeder.ReadCatalogAsync):
/// what SeedAsync writes, and what tools/Avalon.ChunkGen validates its output with.
/// </summary>
public sealed record ChunkCatalogFiles(
    IReadOnlyList<ChunkMetaDto> Chunks,
    IReadOnlyList<(string Path, TownLayoutDto Layout)> Layouts,
    IReadOnlyDictionary<string, PoolMemberDto[]> Pools,
    IReadOnlyDictionary<string, GroupDto[]>? Groups,
    IReadOnlyDictionary<string, SpawnTableEntryDto[]>? SpawnTables,
    IReadOnlyList<(string Path, ProceduralMapDto Map)> ProceduralMaps);

/// <summary>
/// A member of a pool in Maps/chunk-pools.json: a chunk's name (weight 1, no cap), or an object naming the chunk with its
/// weight (left out: 1) and the most times one layout may place it ("max", left out: no limit). The weight also reads
/// "NaN" and "Infinity", and the object's other keys are kept in UnknownKeys, so the seeder refuses them by name (a
/// misspelt "max" would otherwise leave the chunk uncapped) rather than as JSON it cannot read.
/// </summary>
[JsonConverter(typeof(PoolMemberDtoConverter))]
public sealed record PoolMemberDto(string Chunk, float Weight = 1f, int? Max = null)
{
    public IReadOnlyList<string> UnknownKeys { get; init; } = [];
}

/// <summary>Reads a pool member written as a name or as an object; the files are only read.</summary>
public sealed class PoolMemberDtoConverter : JsonConverter<PoolMemberDto>
{
    public override PoolMemberDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? new PoolMemberDto(reader.GetString()!)
            : JsonSerializer.Deserialize<Entry>(ref reader, options) is { } e
                ? new PoolMemberDto(e.Chunk, e.Weight, e.Max) { UnknownKeys = e.Unknown?.Keys.Order(StringComparer.Ordinal).ToArray() ?? [] }
                : null;

    public override void Write(Utf8JsonWriter writer, PoolMemberDto value, JsonSerializerOptions options) =>
        throw new NotSupportedException("chunk-pools.json is only read");

    private sealed record Entry(
        string Chunk,
        [property: JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)] float Weight = 1f,
        int? Max = null)
    {
        [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; init; }
    }
}

/// <summary>One entry of a spawn table in Maps/spawn-tables.json: a slot tag, a creature template, its weight and count range.</summary>
public sealed record SpawnTableEntryDto(string Tag, ulong CreatureId, float Weight, byte Min, byte Max);

/// <summary>A depth band in Maps/ProceduralMaps/&lt;mapId&gt;.json (see ProceduralDepthBand).</summary>
public sealed record DepthBandDto(int MinDepth, int? MaxDepth, ushort MinLevel, ushort MaxLevel);

/// <summary>
/// One procedural map's config, Maps/ProceduralMaps/&lt;mapId&gt;.json; its pool and spawn table by name. Every number and
/// flag but forwardPortalTargetMapId (null: no forward portal), minSetPieceStep (left out: no limit) and doodadRules (left
/// out: none) is required, so a field left out is refused rather than read as 0 or false.
/// </summary>
public sealed record ProceduralMapDto(
    [property: JsonRequired] ushort MapTemplateId, string ChunkPool, string SpawnTable, [property: JsonRequired] ushort MainPathMin,
    [property: JsonRequired] ushort MainPathMax, [property: JsonRequired] float BranchChance, [property: JsonRequired] byte BranchMaxDepth,
    [property: JsonRequired] bool HasBoss, [property: JsonRequired] ushort BackPortalTargetMapId, ushort? ForwardPortalTargetMapId,
    List<DepthBandDto>? DepthBands, ushort? MinSetPieceStep = null, List<DoodadRuleDto>? DoodadRules = null);

/// <summary>
/// A doodad rule in Maps/ProceduralMaps/&lt;mapId&gt;.json (see ProceduralDoodadRule): its template, slot tag, count range,
/// whether it is required, its spacing and depth range, and the spawn's own gates. Every field but the depth range and the
/// gates is required. MinSpacing also reads "NaN" and "Infinity", so the seeder refuses them by name rather than as JSON it
/// cannot read.
/// </summary>
public sealed record DoodadRuleDto(
    [property: JsonRequired] uint DoodadTemplateId, string SlotTag, [property: JsonRequired] byte Min, [property: JsonRequired] byte Max,
    [property: JsonRequired] bool Required,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)] float MinSpacing,
    int? MinDepth, int? MaxDepth,
    DoodadGateDto? VisibleGate = null, DoodadGateDto? UsableGate = null);

/// <summary>A doodad rule's gate: a quest, a DoodadGateCondition by name, and the objective an OnObjective gate names.</summary>
public sealed record DoodadGateDto([property: JsonRequired] uint QuestId, string Condition, uint? ObjectiveId);

/// <summary>One set piece in Maps/chunk-groups.json.</summary>
public sealed record GroupDto(string Name, List<GroupMemberDto> Members);

/// <summary>A set piece's member chunk and its cell in the piece's own frame (0,0 = south-west).</summary>
public sealed record GroupMemberDto(string Chunk, int CellX, int CellZ);

/// <summary>One chunk's metadata file, Maps/Chunks/&lt;name&gt;.json; doodadSlots may be left out (a chunk with none).</summary>
public sealed record ChunkMetaDto(
    string Name,
    string AssetKey,
    byte CellFootprintX,
    byte CellFootprintZ,
    float CellSize,
    Dictionary<string, string[]> Exits,
    List<SpawnSlotDto> SpawnSlots,
    List<PortalSlotDto> PortalSlots,
    string[] Tags,
    List<DoodadSlotDto>? DoodadSlots = null);

public sealed record SpawnSlotDto(string Tag, float LocalX, float LocalY, float LocalZ);

public sealed record PortalSlotDto(string Role, float LocalX, float LocalY, float LocalZ);

/// <summary>
/// A doodad slot: a tag procedural doodad rules name, chunk-local metres, and a yaw of 0, 90, 180 or 270 degrees. The
/// position also reads "NaN" and "Infinity", so the seeder refuses them by name rather than as JSON it cannot read.
/// </summary>
public sealed record DoodadSlotDto(string Tag,
    [property: JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)] float LocalX,
    [property: JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)] float LocalY,
    [property: JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)] float LocalZ,
    int Yaw);

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
