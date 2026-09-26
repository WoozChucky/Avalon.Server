using System.Text.Json;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Seeding;

/// <summary>What one <see cref="ChunkCatalogSeeder.SeedAsync" /> run wrote.</summary>
public sealed record ChunkCatalogSeedResult(int TemplatesAdded, int TemplatesUpdated, int LayoutsReplaced, int PoolsSynced);

/// <summary>
/// Brings the world database's chunk catalog in line with the files committed under Maps/:
/// <c>Chunks/&lt;name&gt;.json</c> (+ its <c>.obj</c>), <c>TownLayouts/*.json</c> and
/// <c>chunk-pools.json</c>. Chunk templates are matched by name, so existing ids (and everything
/// referencing them) survive; templates with no file are left alone. Every file is read and
/// validated before anything is written, and the writes share one transaction.
/// </summary>
public static class ChunkCatalogSeeder
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task<ChunkCatalogSeedResult> SeedAsync(WorldDbContext db, string mapsRoot,
        CancellationToken ct = default)
    {
        List<ChunkMetaDto> chunks = LoadChunks(mapsRoot);
        HashSet<string> chunkNames = chunks.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        List<(string Path, TownLayoutDto Layout)> layouts = LoadLayouts(mapsRoot, chunkNames);
        Dictionary<string, string[]> pools = LoadPools(mapsRoot, chunkNames);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        (int added, int updated) = await UpsertTemplatesAsync(db, chunks, ct);
        Dictionary<string, ChunkTemplate> byName = await db.ChunkTemplates
            .Where(t => chunkNames.Contains(t.Name))
            .ToDictionaryAsync(t => t.Name, StringComparer.Ordinal, ct);

        foreach ((string path, TownLayoutDto layout) in layouts)
            await ReplaceLayoutAsync(db, path, layout, byName, ct);

        foreach ((string pool, string[] members) in pools)
            await SyncPoolAsync(db, pool, members, byName, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ChunkCatalogSeedResult(added, updated, layouts.Count, pools.Count);
    }

    private static List<ChunkMetaDto> LoadChunks(string mapsRoot)
    {
        string dir = Path.Combine(mapsRoot, "Chunks");
        if (!Directory.Exists(dir)) return [];

        List<ChunkMetaDto> chunks = [];
        foreach (string jsonPath in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            ChunkMetaDto meta = Read<ChunkMetaDto>(jsonPath);
            if (meta.Name != Path.GetFileNameWithoutExtension(jsonPath))
                throw new InvalidDataException($"{jsonPath}: name '{meta.Name}' does not match the file name");
            if (!File.Exists(Path.Combine(dir, meta.Name + ".obj")))
                throw new InvalidDataException($"{jsonPath}: chunk '{meta.Name}' has no geometry file {meta.Name}.obj");
            chunks.Add(meta);
        }
        return chunks;
    }

    private static List<(string, TownLayoutDto)> LoadLayouts(string mapsRoot, HashSet<string> chunkNames)
    {
        string dir = Path.Combine(mapsRoot, "TownLayouts");
        if (!Directory.Exists(dir)) return [];

        List<(string, TownLayoutDto)> layouts = [];
        foreach (string path in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            TownLayoutDto layout = Read<TownLayoutDto>(path);
            if (layout.Chunks.Count == 0)
                throw new InvalidDataException($"{path}: chunks empty");
            if (layout.Chunks.Count(c => c.IsEntry) != 1)
                throw new InvalidDataException($"{path}: must have exactly one IsEntry placement");
            var dupes = layout.Chunks.GroupBy(c => (c.GridX, c.GridZ)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0)
                throw new InvalidDataException($"{path}: duplicate (gridX, gridZ): {string.Join(", ", dupes)}");
            var unknown = layout.Chunks.Select(c => c.ChunkName).Where(n => !chunkNames.Contains(n)).Distinct().ToList();
            if (unknown.Count > 0)
                throw new InvalidDataException($"{path}: unknown chunk names: {string.Join(", ", unknown)}");
            layouts.Add((path, layout));
        }
        return layouts;
    }

    private static Dictionary<string, string[]> LoadPools(string mapsRoot, HashSet<string> chunkNames)
    {
        string path = Path.Combine(mapsRoot, "chunk-pools.json");
        if (!File.Exists(path)) return [];

        Dictionary<string, string[]> pools = Read<Dictionary<string, string[]>>(path);
        foreach ((string pool, string[] members) in pools)
        {
            var unknown = members.Where(n => !chunkNames.Contains(n)).ToList();
            if (unknown.Count > 0)
                throw new InvalidDataException($"{path}: pool '{pool}' names unknown chunks: {string.Join(", ", unknown)}");
        }
        return pools;
    }

    private static async Task<(int Added, int Updated)> UpsertTemplatesAsync(WorldDbContext db,
        List<ChunkMetaDto> chunks, CancellationToken ct)
    {
        List<ChunkTemplate> existing = await db.ChunkTemplates.ToListAsync(ct);
        Dictionary<string, ChunkTemplate> byName = existing.ToDictionary(t => t.Name, StringComparer.Ordinal);
        int nextId = existing.Count == 0 ? 1 : existing.Max(t => t.Id.Value) + 1;
        int added = 0, updated = 0;

        foreach (ChunkMetaDto meta in chunks)
        {
            if (!byName.TryGetValue(meta.Name, out ChunkTemplate? target))
            {
                target = new ChunkTemplate { Id = new ChunkTemplateId(nextId++) };
                db.ChunkTemplates.Add(target);
                added++;
            }
            else
            {
                updated++;
            }

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
                .Select(p => new ChunkPortalSlot
                {
                    Role = Enum.Parse<PortalRole>(p.Role, ignoreCase: true),
                    LocalX = p.LocalX,
                    LocalY = p.LocalY,
                    LocalZ = p.LocalZ,
                })
                .ToList();
            target.Tags = meta.Tags;
        }

        await db.SaveChangesAsync(ct);
        return (added, updated);
    }

    private static async Task ReplaceLayoutAsync(WorldDbContext db, string path, TownLayoutDto layout,
        Dictionary<string, ChunkTemplate> byName, CancellationToken ct)
    {
        var mapId = new MapTemplateId((ushort)layout.MapTemplateId);
        MapTemplate template = await db.MapTemplates.FirstOrDefaultAsync(t => t.Id == mapId, ct)
            ?? throw new InvalidDataException($"{path}: MapTemplate {layout.MapTemplateId} not found");
        if (template.MapType != MapType.Town)
            throw new InvalidDataException($"{path}: MapTemplate {layout.MapTemplateId} is {template.MapType}, expected Town");

        foreach (ChunkTemplate chunk in layout.Chunks.Select(c => byName[c.ChunkName]).Distinct())
        {
            if (Math.Abs(chunk.CellSize - layout.CellSize) > 0.001f)
                throw new InvalidDataException(
                    $"{path}: chunk '{chunk.Name}' has CellSize={chunk.CellSize} but layout declares {layout.CellSize}");
        }

        db.MapChunkPlacements.RemoveRange(
            await db.MapChunkPlacements.Where(p => p.MapTemplateId == mapId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        db.MapChunkPlacements.AddRange(layout.Chunks.Select(c => new MapChunkPlacement
        {
            MapTemplateId = mapId,
            ChunkTemplateId = byName[c.ChunkName].Id,
            GridX = c.GridX,
            GridZ = c.GridZ,
            Rotation = c.Rotation,
            IsEntry = c.IsEntry,
            EntryLocalX = c.EntrySpawn?.LocalX ?? 0,
            EntryLocalY = c.EntrySpawn?.LocalY ?? 0,
            EntryLocalZ = c.EntrySpawn?.LocalZ ?? 0,
            BackPortalTargetMapId = c.BackPortalTargetMapId,
            ForwardPortalTargetMapId = c.ForwardPortalTargetMapId,
        }));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SyncPoolAsync(WorldDbContext db, string poolName, string[] members,
        Dictionary<string, ChunkTemplate> byName, CancellationToken ct)
    {
        ChunkPool? pool = await db.ChunkPools.Include(p => p.Memberships)
            .FirstOrDefaultAsync(p => p.Name == poolName, ct);
        if (pool is null)
        {
            List<ushort> ids = await db.ChunkPools.Select(p => p.Id.Value).ToListAsync(ct);
            pool = new ChunkPool
            {
                Id = new ChunkPoolId((ushort)(ids.Count == 0 ? 1 : ids.Max() + 1)),
                Name = poolName,
            };
            db.ChunkPools.Add(pool);
        }

        HashSet<int> wanted = members.Select(n => byName[n].Id.Value).ToHashSet();
        pool.Memberships.RemoveAll(m => !wanted.Contains(m.ChunkTemplateId.Value));
        foreach (int id in wanted.Where(id => pool.Memberships.All(m => m.ChunkTemplateId.Value != id)))
        {
            pool.Memberships.Add(new ChunkPoolMembership
            {
                ChunkPoolId = pool.Id,
                ChunkTemplateId = new ChunkTemplateId(id),
                Weight = 1.0f,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static ushort BuildExitMask(IDictionary<string, string[]> exits)
    {
        ushort mask = 0;
        int[] sideOffsets = [0, 3, 6, 9]; // N, E, S, W
        string[] sides = ["N", "E", "S", "W"];
        string[] slots = ["left", "center", "right"];
        for (int s = 0; s < 4; s++)
        {
            if (!exits.TryGetValue(sides[s], out string[]? arr)) continue;
            foreach (string slot in arr)
            {
                int idx = Array.IndexOf(slots, slot.ToLowerInvariant());
                if (idx >= 0) mask |= (ushort)(1 << (sideOffsets[s] + idx));
            }
        }
        return mask;
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"{path}: empty");

    private sealed record ChunkMetaDto(
        string Name,
        string AssetKey,
        byte CellFootprintX,
        byte CellFootprintZ,
        float CellSize,
        Dictionary<string, string[]> Exits,
        List<SpawnSlotDto> SpawnSlots,
        List<PortalSlotDto> PortalSlots,
        string[] Tags);

    private sealed record SpawnSlotDto(string Tag, float LocalX, float LocalY, float LocalZ);

    private sealed record PortalSlotDto(string Role, float LocalX, float LocalY, float LocalZ);

    private sealed record TownLayoutDto(int MapTemplateId, string MapName, float CellSize, List<TownChunkPlacementDto> Chunks);

    private sealed record TownChunkPlacementDto(
        string ChunkName,
        short GridX,
        short GridZ,
        byte Rotation,
        bool IsEntry,
        EntrySpawnDto? EntrySpawn,
        ushort? BackPortalTargetMapId,
        ushort? ForwardPortalTargetMapId);

    private sealed record EntrySpawnDto(float LocalX, float LocalY, float LocalZ);
}
