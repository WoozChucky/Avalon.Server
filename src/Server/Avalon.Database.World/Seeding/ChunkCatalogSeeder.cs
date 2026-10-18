using System.Globalization;
using System.Text.Json;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Database.World.Seeding;

/// <summary>What one <see cref="ChunkCatalogSeeder.SeedAsync" /> run wrote.</summary>
public sealed record ChunkCatalogSeedResult(
    int TemplatesAdded, int TemplatesUpdated, int LayoutsReplaced, int PoolsSynced, int GroupsReplaced,
    int SpawnTablesSynced, int ProceduralMapsSynced);

/// <summary>
/// Brings the world database's chunk catalog in line with the files committed under Maps/:
/// <c>Chunks/&lt;name&gt;.json</c> (+ its <c>.obj</c>), <c>TownLayouts/*.json</c>,
/// <c>chunk-pools.json</c> and <c>chunk-groups.json</c>. Chunk templates are matched by name, so existing ids (and everything
/// referencing them) survive; templates with no file are left alone. Every file is read and
/// validated before anything is written, and the writes share one transaction.
/// </summary>
public static class ChunkCatalogSeeder
{
    // camelCase only names the files' own properties in an error (a missing required field); reading ignores case.
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<ChunkCatalogSeedResult> SeedAsync(WorldDbContext db, string mapsRoot,
        CancellationToken ct = default)
    {
        ChunkCatalogFiles files = await ReadCatalogAsync(mapsRoot, ct);
        var chunkNames = files.Chunks.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        await ValidateAgainstDatabaseAsync(db, mapsRoot, files, ct);

        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);

        (int added, int updated) = await UpsertTemplatesAsync(db, files.Chunks, ct);
        Dictionary<string, ChunkTemplate> byName = await db.ChunkTemplates
            .Where(t => chunkNames.Contains(t.Name))
            .ToDictionaryAsync(t => t.Name, StringComparer.Ordinal, ct);

        foreach ((string path, TownLayoutDto layout) in files.Layouts)
            await ReplaceLayoutAsync(db, path, layout, byName, ct);

        foreach ((string pool, string[] members) in files.Pools)
            await SyncPoolAsync(db, pool, members, byName, ct);

        if (files.Groups is not null)
            await ReplaceGroupsAsync(db, files.Groups, byName, ct);

        if (files.SpawnTables is not null)
            await UpsertSpawnTablesAsync(db, files.SpawnTables, ct);
        await UpsertProceduralMapsAsync(db, files.ProceduralMaps, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ChunkCatalogSeedResult(added, updated, files.Layouts.Count, files.Pools.Count,
            files.Groups?.Values.Sum(g => g.Length) ?? 0, files.SpawnTables?.Count ?? 0, files.ProceduralMaps.Count);
    }

    /// <summary>
    /// Reads and checks every file under Maps/ without a database, throwing InvalidDataException naming the first
    /// problem. SeedAsync starts with it, and tools/Avalon.ChunkGen validates the pieces it writes with it.
    /// </summary>
    public static async Task<ChunkCatalogFiles> ReadCatalogAsync(string mapsRoot, CancellationToken ct = default)
    {
        List<ChunkMetaDto> chunks = await LoadChunksAsync(mapsRoot, ct);
        var chunkNames = chunks.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        List<(string, TownLayoutDto)> layouts = await LoadLayoutsAsync(mapsRoot, chunkNames, ct);
        Dictionary<string, string[]> pools = await LoadPoolsAsync(mapsRoot, chunkNames, ct);
        Dictionary<string, GroupDto[]>? groups = await LoadGroupsAsync(mapsRoot, chunks, pools, ct);
        Dictionary<string, SpawnTableEntryDto[]>? spawnTables = await LoadSpawnTablesAsync(mapsRoot, ct);
        List<(string, ProceduralMapDto)> maps = await LoadProceduralMapsAsync(mapsRoot, chunks, pools, groups, spawnTables, ct);
        return new ChunkCatalogFiles(chunks, layouts, pools, groups, spawnTables, maps);
    }

    private static async Task<List<ChunkMetaDto>> LoadChunksAsync(string mapsRoot, CancellationToken ct)
    {
        string dir = Path.Combine(mapsRoot, "Chunks");
        if (!Directory.Exists(dir)) return [];

        List<ChunkMetaDto> chunks = [];
        foreach (string jsonPath in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            ChunkMetaDto meta = await ReadAsync<ChunkMetaDto>(jsonPath, ct);
            if (meta.Name != Path.GetFileNameWithoutExtension(jsonPath))
                throw new InvalidDataException($"{jsonPath}: name '{meta.Name}' does not match the file name");
            if (!File.Exists(Path.Combine(dir, meta.Name + ".obj")))
                throw new InvalidDataException($"{jsonPath}: chunk '{meta.Name}' has no geometry file {meta.Name}.obj");
            CheckDoodadSlots(jsonPath, meta);
            chunks.Add(meta);
        }
        return chunks;
    }

    /// <summary>
    /// Refuses a doodad slot with a blank tag, a yaw that is not 0, 90, 180 or 270, a position outside the chunk's own
    /// cells on X/Z (their edges included; NaN is outside), or a height that is not a finite number. Doodad slot tags are
    /// not spawn-table tags: the slot tag check (LoadProceduralMapsAsync) never sees them.
    /// </summary>
    private static void CheckDoodadSlots(string path, ChunkMetaDto meta)
    {
        if (meta.DoodadSlots is null) return;

        float sizeX = meta.CellFootprintX * meta.CellSize, sizeZ = meta.CellFootprintZ * meta.CellSize;
        foreach (DoodadSlotDto slot in meta.DoodadSlots)
        {
            if (string.IsNullOrWhiteSpace(slot.Tag))
                throw new InvalidDataException($"{path}: a doodad slot has no tag");
            if (slot.Yaw is not (0 or 90 or 180 or 270))
                throw new InvalidDataException($"{path}: doodad slot '{slot.Tag}' has yaw {slot.Yaw}, not 0, 90, 180 or 270");
            if (!(slot.LocalX >= 0f && slot.LocalX <= sizeX && slot.LocalZ >= 0f && slot.LocalZ <= sizeZ))
            {
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                    $"{path}: doodad slot '{slot.Tag}' at ({slot.LocalX}, {slot.LocalZ}) lies outside the chunk"));
            }
            if (!float.IsFinite(slot.LocalY))
            {
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                    $"{path}: doodad slot '{slot.Tag}' has localY {slot.LocalY}, not a finite height"));
            }
        }
    }

    private static async Task<List<(string, TownLayoutDto)>> LoadLayoutsAsync(string mapsRoot,
        HashSet<string> chunkNames, CancellationToken ct)
    {
        string dir = Path.Combine(mapsRoot, "TownLayouts");
        if (!Directory.Exists(dir)) return [];

        List<(string, TownLayoutDto)> layouts = [];
        foreach (string path in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            TownLayoutDto layout = await ReadAsync<TownLayoutDto>(path, ct);
            if (layout.Chunks.Count == 0)
                throw new InvalidDataException($"{path}: chunks empty");
            if (layout.Chunks.Count(c => c.IsEntry) != 1)
                throw new InvalidDataException($"{path}: must have exactly one IsEntry placement");
            var dupes = layout.Chunks.GroupBy(c => (c.GridX, c.GridZ)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0)
                throw new InvalidDataException($"{path}: duplicate (gridX, gridZ): {string.Join(", ", dupes)}");
            var unknown = layout.Chunks.Select(c => c.ChunkName).Where(n => !chunkNames.Contains(n)).Distinct(StringComparer.Ordinal).ToList();
            if (unknown.Count > 0)
                throw new InvalidDataException($"{path}: unknown chunk names: {string.Join(", ", unknown)}");
            layouts.Add((path, layout));
        }
        return layouts;
    }

    private static async Task<Dictionary<string, string[]>> LoadPoolsAsync(string mapsRoot,
        HashSet<string> chunkNames, CancellationToken ct)
    {
        string path = Path.Combine(mapsRoot, "chunk-pools.json");
        if (!File.Exists(path)) return [];

        Dictionary<string, string[]> pools = await ReadAsync<Dictionary<string, string[]>>(path, ct);
        foreach ((string pool, string[] members) in pools)
        {
            var unknown = members.Where(n => !chunkNames.Contains(n)).ToList();
            if (unknown.Count > 0)
                throw new InvalidDataException($"{path}: pool '{pool}' names unknown chunks: {string.Join(", ", unknown)}");
        }
        return pools;
    }

    private static async Task<(int Added, int Updated)> UpsertTemplatesAsync(WorldDbContext db,
        IReadOnlyList<ChunkMetaDto> chunks, CancellationToken ct)
    {
        List<ChunkTemplate> existing = await db.ChunkTemplates.ToListAsync(ct);
        var byName = existing.ToDictionary(t => t.Name, StringComparer.Ordinal);
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
            target.DoodadSlots = (meta.DoodadSlots ?? [])
                .Select(d => new ChunkDoodadSlot { Tag = d.Tag, LocalX = d.LocalX, LocalY = d.LocalY, LocalZ = d.LocalZ, Yaw = d.Yaw })
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
            {
                throw new InvalidDataException(
                    $"{path}: chunk '{chunk.Name}' has CellSize={chunk.CellSize} but layout declares {layout.CellSize}");
            }
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

        var wanted = members.Select(n => byName[n].Id.Value).ToHashSet();
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

    /// <summary>
    /// Maps/chunk-groups.json, or null when there is none (the groups in the database are then left alone, as pools
    /// are). Every group must name a pool chunk-pools.json names, a unique name, and 1x1 member chunks that exist, sit in
    /// no pool and no other group, fill a whole rectangle of cells, and declare no exit on a side facing another member.
    /// </summary>
    private static async Task<Dictionary<string, GroupDto[]>?> LoadGroupsAsync(string mapsRoot, IReadOnlyList<ChunkMetaDto> chunks,
        Dictionary<string, string[]> pools, CancellationToken ct)
    {
        string path = Path.Combine(mapsRoot, "chunk-groups.json");
        if (!File.Exists(path)) return null;

        Dictionary<string, GroupDto[]> groups = await ReadAsync<Dictionary<string, GroupDto[]>>(path, ct);
        var metas = chunks.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var pooled = pools.Values.SelectMany(m => m).ToHashSet(StringComparer.Ordinal);
        HashSet<string> groupNames = new(StringComparer.Ordinal);
        HashSet<string> grouped = new(StringComparer.Ordinal);

        foreach ((string pool, GroupDto[] list) in groups)
        {
            if (!pools.ContainsKey(pool))
                throw new InvalidDataException($"{path}: pool '{pool}' is not in chunk-pools.json");

            foreach (GroupDto group in list)
            {
                if (string.IsNullOrWhiteSpace(group.Name))
                    throw new InvalidDataException($"{path}: pool '{pool}' has a group with no name");
                if (group.Members is null)
                    throw new InvalidDataException($"{path}: group '{group.Name}' has no members list");
                if (!groupNames.Add(group.Name))
                    throw new InvalidDataException($"{path}: group '{group.Name}' is defined twice");
                if (group.Members.Count < 2)
                    throw new InvalidDataException($"{path}: group '{group.Name}' needs at least two members");

                foreach (GroupMemberDto member in group.Members)
                {
                    if (!metas.TryGetValue(member.Chunk, out ChunkMetaDto? meta))
                        throw new InvalidDataException($"{path}: group '{group.Name}' names unknown chunk '{member.Chunk}'");
                    if (meta.CellFootprintX != 1 || meta.CellFootprintZ != 1)
                        throw new InvalidDataException($"{path}: group '{group.Name}' member '{member.Chunk}' is not 1x1");
                    if (pooled.Contains(member.Chunk))
                        throw new InvalidDataException($"{path}: group '{group.Name}' member '{member.Chunk}' is also a pool member");
                    if (!grouped.Add(member.Chunk))
                        throw new InvalidDataException($"{path}: chunk '{member.Chunk}' is in two groups");
                    if (member.CellX < 0 || member.CellZ < 0)
                        throw new InvalidDataException($"{path}: group '{group.Name}' member '{member.Chunk}' has a negative cell");
                }

                CheckGroupShape(path, group, metas);
            }
        }

        return groups;
    }

    /// <summary>A group's cells fill a whole rectangle once each, and no member declares an exit facing another member.</summary>
    private static void CheckGroupShape(string path, GroupDto group, Dictionary<string, ChunkMetaDto> metas)
    {
        var cells = group.Members.Select(m => (m.CellX, m.CellZ)).ToHashSet();
        int sizeX = group.Members.Max(m => m.CellX) + 1, sizeZ = group.Members.Max(m => m.CellZ) + 1;
        if (cells.Count != group.Members.Count || cells.Count != sizeX * sizeZ)
            throw new InvalidDataException($"{path}: group '{group.Name}' cells do not fill a {sizeX}x{sizeZ} rectangle once each");

        foreach (GroupMemberDto member in group.Members)
        {
            foreach ((string side, int dx, int dz) in new[] { ("N", 0, 1), ("E", 1, 0), ("S", 0, -1), ("W", -1, 0) })
            {
                bool inner = cells.Contains((member.CellX + dx, member.CellZ + dz));
                if (inner && metas[member.Chunk].Exits.TryGetValue(side, out string[]? slots) && slots.Length > 0)
                {
                    throw new InvalidDataException(
                        $"{path}: group '{group.Name}' member '{member.Chunk}' has an exit on its inner {side} edge");
                }
            }
        }
    }

    /// <summary>Every group is written afresh on each start, like a town layout: nothing refers to a group's id.</summary>
    private static async Task ReplaceGroupsAsync(WorldDbContext db, IReadOnlyDictionary<string, GroupDto[]> groups,
        Dictionary<string, ChunkTemplate> byName, CancellationToken ct)
    {
        db.ChunkGroups.RemoveRange(await db.ChunkGroups.Include(g => g.Members).ToListAsync(ct));
        await db.SaveChangesAsync(ct);

        Dictionary<string, ChunkPoolId> poolIds = await db.ChunkPools.ToDictionaryAsync(p => p.Name, p => p.Id, StringComparer.Ordinal, ct);
        foreach ((string pool, GroupDto[] list) in groups)
        {
            foreach (GroupDto group in list)
            {
                db.ChunkGroups.Add(new ChunkGroup
                {
                    Name = group.Name,
                    ChunkPoolId = poolIds[pool],
                    Members = group.Members.Select(m => new ChunkGroupMember
                    {
                        ChunkTemplateId = byName[m.Chunk].Id,
                        CellX = (byte)m.CellX,
                        CellZ = (byte)m.CellZ,
                    }).ToList(),
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Slot tags no spawn table answers for: the player's entry and an explicitly empty slot.</summary>
    private static readonly string[] s_untabledSlotTags = ["entry", "empty"];

    /// <summary>
    /// Spawn-table tags no slot carries, each rolled at the slot whose tag it names: a leader's pack is rolled at its
    /// leader's slot (CreaturePlacementService). Confirmed by the owner.
    /// </summary>
    private static readonly Dictionary<string, string> s_companionTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["leader_pack"] = "leader",
    };

    /// <summary>Maps/spawn-tables.json, or null when there is none (spawn tables in the database are then left alone).</summary>
    private static async Task<Dictionary<string, SpawnTableEntryDto[]>?> LoadSpawnTablesAsync(string mapsRoot, CancellationToken ct)
    {
        string path = Path.Combine(mapsRoot, "spawn-tables.json");
        if (!File.Exists(path)) return null;

        Dictionary<string, SpawnTableEntryDto[]> tables = await ReadAsync<Dictionary<string, SpawnTableEntryDto[]>>(path, ct);
        foreach ((string name, SpawnTableEntryDto[] entries) in tables)
        {
            if (entries.Length == 0)
                throw new InvalidDataException($"{path}: spawn table '{name}' has no entries");
            foreach (SpawnTableEntryDto entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Tag))
                    throw new InvalidDataException($"{path}: spawn table '{name}' has an entry with no tag");
                if (!(entry.Weight > 0f) || float.IsInfinity(entry.Weight))
                    throw new InvalidDataException($"{path}: spawn table '{name}' entry '{entry.Tag}' has weight {entry.Weight}");
                if (entry.Min < 1 || entry.Max < entry.Min)
                    throw new InvalidDataException($"{path}: spawn table '{name}' entry '{entry.Tag}' has count {entry.Min}-{entry.Max}");
            }
        }

        return tables;
    }

    /// <summary>
    /// Maps/ProceduralMaps/*.json. Each file is named after its mapTemplateId, names a pool chunk-pools.json names and a
    /// spawn table spawn-tables.json names, and its spawn table's tags must match the slot tags its pool's chunks (set
    /// pieces included) use: every entry tag is a slot tag (or a companion of one), and every slot tag but entry and
    /// empty has an entry. Whether the map exists and is Normal, and whether each creature exists, is checked against
    /// the database by ValidateAgainstDatabaseAsync.
    /// </summary>
    private static async Task<List<(string, ProceduralMapDto)>> LoadProceduralMapsAsync(string mapsRoot,
        IReadOnlyList<ChunkMetaDto> chunks, IReadOnlyDictionary<string, string[]> pools,
        IReadOnlyDictionary<string, GroupDto[]>? groups, IReadOnlyDictionary<string, SpawnTableEntryDto[]>? tables,
        CancellationToken ct)
    {
        string dir = Path.Combine(mapsRoot, "ProceduralMaps");
        if (!Directory.Exists(dir)) return [];

        var metas = chunks.ToDictionary(c => c.Name, StringComparer.Ordinal);
        List<(string, ProceduralMapDto)> maps = [];
        foreach (string path in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            ProceduralMapDto map = await ReadAsync<ProceduralMapDto>(path, ct);
            if (string.IsNullOrWhiteSpace(map.ChunkPool))
                throw new InvalidDataException($"{path}: chunkPool is missing");
            if (string.IsNullOrWhiteSpace(map.SpawnTable))
                throw new InvalidDataException($"{path}: spawnTable is missing");
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), map.MapTemplateId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                throw new InvalidDataException($"{path}: mapTemplateId {map.MapTemplateId} does not match the file name");
            if (!pools.TryGetValue(map.ChunkPool, out string[]? members))
                throw new InvalidDataException($"{path}: pool '{map.ChunkPool}' is not in chunk-pools.json");
            if (tables is null || !tables.TryGetValue(map.SpawnTable, out SpawnTableEntryDto[]? entries))
                throw new InvalidDataException($"{path}: spawn table '{map.SpawnTable}' is not in spawn-tables.json");

            IEnumerable<string> chunkNames = members.Concat(
                groups?.GetValueOrDefault(map.ChunkPool)?.SelectMany(g => g.Members.Select(m => m.Chunk)) ?? []);
            var slotTags = chunkNames
                .SelectMany(n => metas[n].SpawnSlots.Select(s => s.Tag))
                .Where(t => !s_untabledSlotTags.Contains(t, StringComparer.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var entryTags = entries.Select(e => e.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string tag in entryTags)
            {
                bool companion = s_companionTags.TryGetValue(tag, out string? of) && slotTags.Contains(of);
                if (!slotTags.Contains(tag) && !companion)
                    throw new InvalidDataException($"{path}: spawn table '{map.SpawnTable}' tag '{tag}' matches no slot in pool '{map.ChunkPool}'");
            }
            foreach (string tag in slotTags.Where(t => !entryTags.Contains(t)))
                throw new InvalidDataException($"{path}: pool '{map.ChunkPool}' has '{tag}' slots but spawn table '{map.SpawnTable}' has no '{tag}' entry");

            CheckDoodadRules(path, map, chunkNames.Distinct(StringComparer.Ordinal).Select(n => metas[n]).ToList());
            maps.Add((path, map));
        }

        return maps;
    }

    /// <summary>The most doodads one rule may place in a run.</summary>
    private const int MaxDoodadsPerRule = 50;

    /// <summary>
    /// A map's doodad rules, against its pool's chunks and set pieces: each names a tag some doodad slot there carries, a
    /// count range of at most 50, a finite spacing of 0 or more, a depth range that is not reversed, and gates whose
    /// condition is a DoodadGateCondition by name (an objective with OnObjective and with nothing else). A required rule
    /// may not want more doodads than the pool has slots of its tag that its depth range can reach: a chunk can stand at
    /// any depth but the entry's, 0, which only an entry-capable chunk takes. Whether the template exists and is solid is
    /// checked against the database.
    /// </summary>
    private static void CheckDoodadRules(string path, ProceduralMapDto map, IReadOnlyList<ChunkMetaDto> chunks)
    {
        if (map.DoodadRules is not { } rules) return;

        for (int i = 0; i < rules.Count; i++)
        {
            DoodadRuleDto rule = rules[i];
            string name = $"doodad rule {i + 1} (template {rule.DoodadTemplateId})";
            if (string.IsNullOrWhiteSpace(rule.SlotTag))
                throw new InvalidDataException($"{path}: {name} has no slotTag");
            if (rule.Min > rule.Max || rule.Max > MaxDoodadsPerRule)
                throw new InvalidDataException($"{path}: {name} has count {rule.Min}-{rule.Max}; it must be 0-{MaxDoodadsPerRule}, min at most max");
            if (!(rule.MinSpacing >= 0f) || float.IsInfinity(rule.MinSpacing))
            {
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                    $"{path}: {name} has minSpacing {rule.MinSpacing}; it must be finite and 0 or more"));
            }

            if (rule.MinDepth is < 0)
                throw new InvalidDataException($"{path}: {name} has minDepth {rule.MinDepth}; a depth is 0 or more");
            if (rule.MaxDepth is < 0)
                throw new InvalidDataException($"{path}: {name} has maxDepth {rule.MaxDepth}; a depth is 0 or more");
            if (rule.MinDepth is { } from && rule.MaxDepth is { } to && from > to)
                throw new InvalidDataException($"{path}: {name} has minDepth {from} above maxDepth {to}");
            CheckDoodadGate(path, name, "visibleGate", rule.VisibleGate);
            CheckDoodadGate(path, name, "usableGate", rule.UsableGate);

            if (SlotsTagged(chunks, rule.SlotTag) == 0)
                throw new InvalidDataException($"{path}: {name} names slot tag '{rule.SlotTag}', which no doodad slot in pool '{map.ChunkPool}' carries");

            if (!rule.Required) continue;
            int reachable = SlotsTagged(rule.MaxDepth == 0 ? chunks.Where(IsEntryCapable) : chunks, rule.SlotTag);
            if (rule.Max > reachable)
            {
                throw new InvalidDataException(
                    $"{path}: required {name} wants up to {rule.Max} doodads, but pool '{map.ChunkPool}' has only {reachable} '{rule.SlotTag}' slots its depth range reaches");
            }
        }
    }

    private static int SlotsTagged(IEnumerable<ChunkMetaDto> chunks, string tag) =>
        chunks.Sum(c => (c.DoodadSlots ?? []).Count(d => string.Equals(d.Tag, tag, StringComparison.OrdinalIgnoreCase)));

    /// <summary>What ProceduralLayoutGenerator may put at the entry: a chunk with an entry spawn slot and a back portal.</summary>
    private static bool IsEntryCapable(ChunkMetaDto chunk) =>
        chunk.SpawnSlots.Any(s => string.Equals(s.Tag, "entry", StringComparison.OrdinalIgnoreCase))
        && chunk.PortalSlots.Any(p => string.Equals(p.Role, nameof(PortalRole.Back), StringComparison.OrdinalIgnoreCase));

    private static void CheckDoodadGate(string path, string rule, string field, DoodadGateDto? gate)
    {
        if (gate is null) return;

        if (DoodadGateConditionByName(gate.Condition) is not { } condition)
            throw new InvalidDataException($"{path}: {rule} {field} has condition '{gate.Condition}', which is not a doodad gate condition");
        if (condition == DoodadGateCondition.OnObjective && gate.ObjectiveId is null)
            throw new InvalidDataException($"{path}: {rule} {field} is OnObjective but names no objectiveId");
        if (condition != DoodadGateCondition.OnObjective && gate.ObjectiveId is not null)
            throw new InvalidDataException($"{path}: {rule} {field} is {condition} but names objectiveId {gate.ObjectiveId}; only OnObjective names one");
    }

    /// <summary>A DoodadGateCondition by its name, ignoring case; never by number, so "2" or "99" is no condition.</summary>
    private static DoodadGateCondition? DoodadGateConditionByName(string? name)
    {
        foreach (DoodadGateCondition condition in Enum.GetValues<DoodadGateCondition>())
        {
            if (string.Equals(condition.ToString(), name, StringComparison.OrdinalIgnoreCase))
                return condition;
        }

        return null;
    }

    /// <summary>The checks that need the database, made before the transaction opens, so a refusal writes nothing.</summary>
    private static async Task ValidateAgainstDatabaseAsync(WorldDbContext db, string mapsRoot, ChunkCatalogFiles files,
        CancellationToken ct)
    {
        var mapTypes = (await db.MapTemplates.AsNoTracking().ToListAsync(ct))
            .ToDictionary(m => m.Id.Value, m => m.MapType);
        // A band above the highest base-stat row would spawn creatures reporting its level with that row's stats.
        int highestStatLevel = files.ProceduralMaps.Any(m => m.Map.DepthBands is { Count: > 0 })
            ? await db.CreatureBaseStats.Select(s => (int?)s.Level).MaxAsync(ct) ?? 0
            : 0;
        foreach ((string path, ProceduralMapDto map) in files.ProceduralMaps)
        {
            if (!mapTypes.TryGetValue(map.MapTemplateId, out MapType type))
                throw new InvalidDataException($"{path}: MapTemplate {map.MapTemplateId} not found");
            if (type != MapType.Normal)
                throw new InvalidDataException($"{path}: MapTemplate {map.MapTemplateId} is {type}, expected Normal");
            if (!mapTypes.ContainsKey(map.BackPortalTargetMapId))
                throw new InvalidDataException($"{path}: backPortalTargetMapId {map.BackPortalTargetMapId} names no MapTemplate");
            if (map.ForwardPortalTargetMapId is { } forward && !mapTypes.ContainsKey(forward))
                throw new InvalidDataException($"{path}: forwardPortalTargetMapId {forward} names no MapTemplate");
            foreach (DepthBandDto band in (map.DepthBands ?? []).Where(b => b.MaxLevel > highestStatLevel))
            {
                throw new InvalidDataException(
                    $"{path}: band from depth {band.MinDepth} has maxLevel {band.MaxLevel}, above the highest CreatureBaseStats level {highestStatLevel}");
            }
        }

        await ValidateDoodadRulesAsync(db, files, ct);

        if (files.SpawnTables is null) return;
        string tablesPath = Path.Combine(mapsRoot, "spawn-tables.json");
        var creatures = (await db.CreatureTemplates.AsNoTracking().ToListAsync(ct)).Select(t => t.Id.Value).ToHashSet();
        foreach ((string name, SpawnTableEntryDto[] entries) in files.SpawnTables)
        {
            foreach (SpawnTableEntryDto entry in entries.Where(e => !creatures.Contains(e.CreatureId)))
                throw new InvalidDataException($"{tablesPath}: spawn table '{name}' names creature template {entry.CreatureId}, which does not exist");
        }
    }

    /// <summary>The doodad slot tag kept furthest from blockers, paths and spawns: the only one a solid doodad's box fits.</summary>
    private const string LargeDoodadSlotTag = "doodad_large";

    /// <summary>
    /// Each doodad rule's template exists, and a solid one (Collision not 0) stands only at a doodad_large slot, the only one its
    /// box fits; is not required, since a required doodad may stand on open navmesh
    /// its box was never baked into; and is not gated, since a solid doodad is never hidden.
    /// </summary>
    private static async Task ValidateDoodadRulesAsync(WorldDbContext db, ChunkCatalogFiles files, CancellationToken ct)
    {
        if (!files.ProceduralMaps.Any(m => m.Map.DoodadRules is { Count: > 0 })) return;

        var collisions = (await db.DoodadTemplates.AsNoTracking().ToListAsync(ct))
            .ToDictionary(t => t.Id.Value, t => t.Collision);
        foreach ((string path, ProceduralMapDto map) in files.ProceduralMaps)
        {
            List<DoodadRuleDto> rules = map.DoodadRules ?? [];
            for (int i = 0; i < rules.Count; i++)
            {
                DoodadRuleDto rule = rules[i];
                string name = $"doodad rule {i + 1}";
                if (!collisions.TryGetValue(rule.DoodadTemplateId, out DoodadCollision collision))
                    throw new InvalidDataException($"{path}: {name} names doodad template {rule.DoodadTemplateId}, which does not exist");
                if (collision == DoodadCollision.None) continue;
                if (!string.Equals(rule.SlotTag, LargeDoodadSlotTag, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"{path}: {name} puts doodad template {rule.DoodadTemplateId}, which is solid, at '{rule.SlotTag}' slots; a solid doodad stands only at '{LargeDoodadSlotTag}' slots");
                }
                if (rule.Required)
                    throw new InvalidDataException($"{path}: required {name} names doodad template {rule.DoodadTemplateId}, which is solid");
                if (rule.VisibleGate is not null || rule.UsableGate is not null)
                    throw new InvalidDataException($"{path}: {name} gates doodad template {rule.DoodadTemplateId}, which is solid and never hidden");
            }
        }
    }

    /// <summary>
    /// Spawn tables are matched by name, so an existing table keeps its id (the forest's is 1) and a new one gets the
    /// highest id + 1; a table's entries are replaced. The old entries are deleted and saved before the new ones are
    /// added, so the new rows take fresh identity values. A table in the database with no file entry is left alone.
    /// </summary>
    private static async Task UpsertSpawnTablesAsync(WorldDbContext db, IReadOnlyDictionary<string, SpawnTableEntryDto[]> tables,
        CancellationToken ct)
    {
        List<SpawnTable> existing = await db.SpawnTables.ToListAsync(ct);
        ushort next = (ushort)(existing.Count == 0 ? 1 : existing.Max(t => t.Id.Value) + 1);

        foreach ((string name, SpawnTableEntryDto[] entries) in tables)
        {
            SpawnTable? table = existing.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
            if (table is null)
            {
                table = new SpawnTable { Id = new SpawnTableId(next++), Name = name };
                db.SpawnTables.Add(table);
            }
            else
            {
                table.Entries.Clear();
                await db.SaveChangesAsync(ct);
            }

            table.Entries.AddRange(entries.Select(e => new SpawnTableEntry
            {
                SpawnTableId = table.Id,
                Tag = e.Tag,
                CreatureId = new CreatureTemplateId(e.CreatureId),
                Weight = e.Weight,
                MinCount = e.Min,
                MaxCount = e.Max,
            }));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Configs are matched by MapTemplateId (map 2's existing row is updated in place) and their depth bands and doodad
    /// rules replaced, each rule's Sequence its place in the file from 1; the pool and spawn table are resolved by name. A
    /// config in the database with no file is left alone.
    /// </summary>
    private static async Task UpsertProceduralMapsAsync(WorldDbContext db, IReadOnlyList<(string Path, ProceduralMapDto Map)> maps,
        CancellationToken ct)
    {
        if (maps.Count == 0) return;
        Dictionary<string, ChunkPoolId> pools = await db.ChunkPools.ToDictionaryAsync(p => p.Name, p => p.Id, StringComparer.Ordinal, ct);
        Dictionary<string, SpawnTableId> tables = await db.SpawnTables.ToDictionaryAsync(t => t.Name, t => t.Id, StringComparer.Ordinal, ct);

        foreach ((_, ProceduralMapDto map) in maps)
        {
            var id = new MapTemplateId(map.MapTemplateId);
            ProceduralMapConfig? config = await db.ProceduralMapConfigs.FirstOrDefaultAsync(c => c.MapTemplateId == id, ct);
            if (config is null)
            {
                config = new ProceduralMapConfig { MapTemplateId = id };
                db.ProceduralMapConfigs.Add(config);
            }
            else if (config.DepthBands.Count > 0 || config.DoodadRules.Count > 0)
            {
                // Saved before the new rows are added: a band or a rule keeps its key (MinDepth, Sequence) across starts.
                config.DepthBands.Clear();
                config.DoodadRules.Clear();
                await db.SaveChangesAsync(ct);
            }

            config.ChunkPoolId = pools[map.ChunkPool];
            config.SpawnTableId = tables[map.SpawnTable];
            config.MainPathMin = map.MainPathMin;
            config.MainPathMax = map.MainPathMax;
            config.BranchChance = map.BranchChance;
            config.BranchMaxDepth = map.BranchMaxDepth;
            config.HasBoss = map.HasBoss;
            config.BackPortalTargetMapId = map.BackPortalTargetMapId;
            config.ForwardPortalTargetMapId = map.ForwardPortalTargetMapId;
            config.MinSetPieceStep = map.MinSetPieceStep ?? 0;
            config.DepthBands.AddRange((map.DepthBands ?? []).Select(b => new ProceduralDepthBand
            {
                MinDepth = b.MinDepth,
                MaxDepth = b.MaxDepth,
                MinLevel = b.MinLevel,
                MaxLevel = b.MaxLevel,
            }));
            config.DoodadRules.AddRange((map.DoodadRules ?? []).Select((r, i) => new ProceduralDoodadRule
            {
                Sequence = i + 1,
                DoodadTemplateId = new DoodadTemplateId(r.DoodadTemplateId),
                SlotTag = r.SlotTag,
                MinCount = r.Min,
                MaxCount = r.Max,
                Required = r.Required,
                MinSpacing = r.MinSpacing,
                MinDepth = r.MinDepth,
                MaxDepth = r.MaxDepth,
                VisibleQuestId = r.VisibleGate?.QuestId,
                VisibleCondition = DoodadGateConditionByName(r.VisibleGate?.Condition),
                VisibleObjectiveId = r.VisibleGate?.ObjectiveId,
                UsableQuestId = r.UsableGate?.QuestId,
                UsableCondition = DoodadGateConditionByName(r.UsableGate?.Condition),
                UsableObjectiveId = r.UsableGate?.ObjectiveId,
            }));
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

    /// <summary>Reads one file; JSON it cannot read, a required field left out included, is refused naming the file.</summary>
    private static async Task<T> ReadAsync<T>(string path, CancellationToken ct)
    {
        string text = await File.ReadAllTextAsync(path, ct);
        try
        {
            return JsonSerializer.Deserialize<T>(text, s_json) ?? throw new InvalidDataException($"{path}: empty");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }
    }
}
