using System.Text.Json;
using Avalon.Configuration;
using Avalon.Database.World;
using Avalon.Domain.World;
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
    ? await ctx.ChunkTemplates.MaxAsync(t => (int)t.Id.Value) + 1
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
return 0;

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
