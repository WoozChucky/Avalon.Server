using System.Text.Json;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var exportDir = args.Length > 0 ? args[0] : "chunks-export";
var chunksOutDir = "src/Server/Avalon.Server.World/Maps/Chunks";
Directory.CreateDirectory(chunksOutDir);

if (!Directory.Exists(exportDir))
{
    Console.WriteLine($"Export directory '{exportDir}' does not exist. Nothing to import.");
    return 0;
}

using var ctx = BuildDbContext();
int added = 0, updated = 0;

int nextId = (await ctx.ChunkTemplates.AnyAsync())
    ? (await ctx.ChunkTemplates.Select(t => t.Id).ToListAsync()).Max(id => id.Value) + 1
    : 1;

foreach (var dir in Directory.EnumerateDirectories(exportDir))
{
    var jsonPath = Path.Combine(dir, "chunk.json");
    var objPath  = Path.Combine(dir, "chunk.obj");
    if (!File.Exists(jsonPath) || !File.Exists(objPath))
    {
        Console.WriteLine($"Skipping '{dir}': missing chunk.json or chunk.obj");
        continue;
    }

    var meta = JsonSerializer.Deserialize<ChunkMetaDto>(File.ReadAllText(jsonPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException(jsonPath);

    var existing = await ctx.ChunkTemplates.FirstOrDefaultAsync(t => t.Name == meta.Name);
    var target = existing ?? new ChunkTemplate { Id = new Avalon.Common.ValueObjects.ChunkTemplateId(nextId++) };
    target.Name = meta.Name;
    target.AssetKey = meta.AssetKey;
    target.GeometryFile = $"Chunks/{meta.Name}.obj";
    target.CellFootprintX = meta.CellFootprintX;
    target.CellFootprintZ = meta.CellFootprintZ;
    target.CellSize = meta.CellSize;
    target.Exits = BuildExitMask(meta.Exits);
    target.SpawnSlots = meta.SpawnSlots
        .Select(s => new ChunkSpawnSlot { Tag = s.Tag, LocalX = s.LocalX, LocalY = s.LocalY, LocalZ = s.LocalZ })
        .ToList();
    target.PortalSlots = meta.PortalSlots
        .Select(p => new ChunkPortalSlot { Role = Enum.Parse<PortalRole>(p.Role, ignoreCase: true), LocalX = p.LocalX, LocalY = p.LocalY, LocalZ = p.LocalZ })
        .ToList();
    target.Tags = meta.Tags;

    File.Copy(objPath, Path.Combine(chunksOutDir, $"{meta.Name}.obj"), overwrite: true);

    if (existing is null) { ctx.ChunkTemplates.Add(target); added++; }
    else { ctx.ChunkTemplates.Update(target); updated++; }
}

await ctx.SaveChangesAsync();
Console.WriteLine($"Imported {added} new, {updated} updated chunks.");

var townLayoutsDir = Path.Combine(exportDir, "town_layouts");
int layoutsImported = 0;
if (Directory.Exists(townLayoutsDir))
{
    foreach (var jsonFile in Directory.EnumerateFiles(townLayoutsDir, "*.json"))
    {
        try
        {
            var dto = JsonSerializer.Deserialize<TownLayoutImportDto>(
                File.ReadAllText(jsonFile),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException(jsonFile);

            await ImportTownLayoutAsync(ctx, dto, jsonFile);
            layoutsImported++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to import {jsonFile}: {ex.Message}");
        }
    }
}
Console.WriteLine($"Imported {layoutsImported} town layout(s).");
return 0;

static async Task ImportTownLayoutAsync(WorldDbContext ctx, TownLayoutImportDto dto, string sourcePath)
{
    var mapId = new MapTemplateId((ushort)dto.MapTemplateId);
    var template = await ctx.MapTemplates.FirstOrDefaultAsync(t => t.Id == mapId)
        ?? throw new InvalidDataException($"{sourcePath}: MapTemplate {dto.MapTemplateId} not found");
    if (template.MapType != MapType.Town)
        throw new InvalidDataException($"{sourcePath}: MapTemplate {dto.MapTemplateId} is {template.MapType}, expected Town");

    if (dto.Chunks.Count == 0)
        throw new InvalidDataException($"{sourcePath}: chunks empty");
    if (dto.Chunks.Count(c => c.IsEntry) != 1)
        throw new InvalidDataException($"{sourcePath}: must have exactly one IsEntry placement");

    var dupes = dto.Chunks
        .GroupBy(c => (c.GridX, c.GridZ))
        .Where(g => g.Count() > 1)
        .Select(g => g.Key)
        .ToList();
    if (dupes.Count > 0)
        throw new InvalidDataException($"{sourcePath}: duplicate (gridX, gridZ): {string.Join(", ", dupes)}");

    var names = dto.Chunks.Select(c => c.ChunkName).Distinct().ToList();
    var chunks = await ctx.ChunkTemplates.Where(c => names.Contains(c.Name)).ToListAsync();
    if (chunks.Count != names.Count)
    {
        var missing = names.Except(chunks.Select(c => c.Name));
        throw new InvalidDataException($"{sourcePath}: unknown chunk names: {string.Join(", ", missing)}");
    }

    foreach (var c in chunks)
    {
        if (Math.Abs(c.CellSize - dto.CellSize) > 0.001f)
            throw new InvalidDataException(
                $"{sourcePath}: chunk '{c.Name}' has CellSize={c.CellSize} but layout declares {dto.CellSize}");
    }

    var byName = chunks.ToDictionary(c => c.Name, c => c.Id);

    await using var tx = await ctx.Database.BeginTransactionAsync();
    var existing = await ctx.MapChunkPlacements
        .Where(p => p.MapTemplateId == mapId).ToListAsync();
    ctx.MapChunkPlacements.RemoveRange(existing);
    await ctx.SaveChangesAsync();

    var inserts = dto.Chunks.Select(c => new MapChunkPlacement
    {
        MapTemplateId = mapId,
        ChunkTemplateId = byName[c.ChunkName],
        GridX = c.GridX,
        GridZ = c.GridZ,
        Rotation = c.Rotation,
        IsEntry = c.IsEntry,
        EntryLocalX = c.EntrySpawn?.LocalX ?? 0,
        EntryLocalY = c.EntrySpawn?.LocalY ?? 0,
        EntryLocalZ = c.EntrySpawn?.LocalZ ?? 0,
    }).ToList();

    await ctx.MapChunkPlacements.AddRangeAsync(inserts);
    await ctx.SaveChangesAsync();
    await tx.CommitAsync();

    Console.WriteLine($"  {sourcePath}: replaced {existing.Count}, inserted {inserts.Count} placements for MapId {dto.MapTemplateId}");
}

static ushort BuildExitMask(IDictionary<string, string[]> exits)
{
    ushort mask = 0;
    int[] sideOffsets = { 0, 3, 6, 9 }; // N, E, S, W
    string[] sides = { "N", "E", "S", "W" };
    string[] slots = { "left", "center", "right" };
    for (int s = 0; s < 4; s++)
    {
        if (!exits.TryGetValue(sides[s], out var arr)) continue;
        foreach (var slot in arr)
        {
            int idx = Array.IndexOf(slots, slot.ToLowerInvariant());
            if (idx >= 0) mask |= (ushort)(1 << (sideOffsets[s] + idx));
        }
    }
    return mask;
}

static WorldDbContext BuildDbContext()
{
    var config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.Design.json", optional: true)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    var dbConfig = new DatabaseConfiguration();
    config.GetSection("Database").Bind(dbConfig);

    var conn = dbConfig.World?.ConnectionString
        ?? config["Database:World:ConnectionString"]
        ?? throw new InvalidOperationException(
            "World connection string not configured. Set Database:World:ConnectionString in appsettings.Design.json or Database__World__ConnectionString env var.");

    var opts = Options.Create(new DatabaseConfiguration
    {
        World = new DatabaseConnection { ConnectionString = conn }
    });

    return new WorldDbContext(NullLoggerFactory.Instance, opts);
}

record ChunkMetaDto(
    string Name,
    string AssetKey,
    byte CellFootprintX,
    byte CellFootprintZ,
    float CellSize,
    Dictionary<string, string[]> Exits,
    List<SpawnSlotDto> SpawnSlots,
    List<PortalSlotDto> PortalSlots,
    string[] Tags);

record SpawnSlotDto(string Tag, float LocalX, float LocalY, float LocalZ);
record PortalSlotDto(string Role, float LocalX, float LocalY, float LocalZ);

record TownLayoutImportDto(
    int MapTemplateId,
    string MapName,
    float CellSize,
    List<TownChunkPlacementDto> Chunks);

record TownChunkPlacementDto(
    string ChunkName,
    short GridX,
    short GridZ,
    byte Rotation,
    bool IsEntry,
    EntrySpawnDto? EntrySpawn);

record EntrySpawnDto(float LocalX, float LocalY, float LocalZ);
