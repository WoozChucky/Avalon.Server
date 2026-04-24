using System.Text.Json;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

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
    var target = existing ?? new ChunkTemplate();
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
    // TODO(Task 24 scaffold): Fill in the real context construction by copying the body of
    //   src/Server/Avalon.Database.World/WorldDbContext.cs → CharacterDbContextFactory.CreateDbContext.
    // It loads appsettings.Design.json + env vars, binds DatabaseConfiguration, resolves World.ConnectionString,
    // creates a NullLoggerFactory, and constructs WorldDbContext(loggerFactory, Options.Create(dbConfig)).
    // When the first real chunks arrive from Unity, fill this in before running the importer.
    throw new NotImplementedException(
        "BuildDbContext is a scaffold. See CharacterDbContextFactory.CreateDbContext for the pattern to copy.");
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
