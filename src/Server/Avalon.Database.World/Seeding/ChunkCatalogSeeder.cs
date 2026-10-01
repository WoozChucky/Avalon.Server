using System.Globalization;
using System.Text.Json;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;

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
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task<ChunkCatalogSeedResult> SeedAsync(WorldDbContext db, string mapsRoot,
        CancellationToken ct = default)
    {
        ChunkCatalogFiles files = await ReadCatalogAsync(mapsRoot, ct);
        HashSet<string> chunkNames = files.Chunks.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        await ValidateAgainstDatabaseAsync(db, mapsRoot, files, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

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
        HashSet<string> chunkNames = chunks.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
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
            chunks.Add(meta);
        }
        return chunks;
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
            var unknown = layout.Chunks.Select(c => c.ChunkName).Where(n => !chunkNames.Contains(n)).Distinct().ToList();
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
        Dictionary<string, ChunkMetaDto> metas = chunks.ToDictionary(c => c.Name, StringComparer.Ordinal);
        HashSet<string> pooled = pools.Values.SelectMany(m => m).ToHashSet(StringComparer.Ordinal);
        HashSet<string> groupNames = new(StringComparer.Ordinal);
        HashSet<string> grouped = new(StringComparer.Ordinal);

        foreach ((string pool, GroupDto[] list) in groups)
        {
            if (!pools.ContainsKey(pool))
                throw new InvalidDataException($"{path}: pool '{pool}' is not in chunk-pools.json");

            foreach (GroupDto group in list)
            {
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
                            throw new InvalidDataException(
                                $"{path}: group '{group.Name}' member '{member.Chunk}' has an exit on its inner {side} edge");
                    }
                }
            }
        }

        return groups;
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
    private static readonly string[] UntabledSlotTags = ["entry", "empty"];

    /// <summary>
    /// Spawn-table tags no slot carries, each rolled at the slot whose tag it names: a leader's pack is rolled at its
    /// leader's slot (CreaturePlacementService). Confirmed by the owner.
    /// </summary>
    private static readonly Dictionary<string, string> CompanionTags = new(StringComparer.OrdinalIgnoreCase)
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

        Dictionary<string, ChunkMetaDto> metas = chunks.ToDictionary(c => c.Name, StringComparer.Ordinal);
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
            HashSet<string> slotTags = chunkNames
                .SelectMany(n => metas[n].SpawnSlots.Select(s => s.Tag))
                .Where(t => !UntabledSlotTags.Contains(t, StringComparer.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            HashSet<string> entryTags = entries.Select(e => e.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string tag in entryTags)
            {
                bool companion = CompanionTags.TryGetValue(tag, out string? of) && slotTags.Contains(of);
                if (!slotTags.Contains(tag) && !companion)
                    throw new InvalidDataException($"{path}: spawn table '{map.SpawnTable}' tag '{tag}' matches no slot in pool '{map.ChunkPool}'");
            }
            foreach (string tag in slotTags.Where(t => !entryTags.Contains(t)))
                throw new InvalidDataException($"{path}: pool '{map.ChunkPool}' has '{tag}' slots but spawn table '{map.SpawnTable}' has no '{tag}' entry");

            maps.Add((path, map));
        }

        return maps;
    }

    /// <summary>The checks that need the database, made before the transaction opens, so a refusal writes nothing.</summary>
    private static async Task ValidateAgainstDatabaseAsync(WorldDbContext db, string mapsRoot, ChunkCatalogFiles files,
        CancellationToken ct)
    {
        Dictionary<ushort, MapType> mapTypes = (await db.MapTemplates.AsNoTracking().ToListAsync(ct))
            .ToDictionary(m => m.Id.Value, m => m.MapType);
        foreach ((string path, ProceduralMapDto map) in files.ProceduralMaps)
        {
            if (!mapTypes.TryGetValue(map.MapTemplateId, out MapType type))
                throw new InvalidDataException($"{path}: MapTemplate {map.MapTemplateId} not found");
            if (type != MapType.Normal)
                throw new InvalidDataException($"{path}: MapTemplate {map.MapTemplateId} is {type}, expected Normal");
        }

        if (files.SpawnTables is null) return;
        string tablesPath = Path.Combine(mapsRoot, "spawn-tables.json");
        HashSet<ulong> creatures = (await db.CreatureTemplates.AsNoTracking().ToListAsync(ct)).Select(t => t.Id.Value).ToHashSet();
        foreach ((string name, SpawnTableEntryDto[] entries) in files.SpawnTables)
        {
            foreach (SpawnTableEntryDto entry in entries.Where(e => !creatures.Contains(e.CreatureId)))
                throw new InvalidDataException($"{tablesPath}: spawn table '{name}' names creature template {entry.CreatureId}, which does not exist");
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
    /// Configs are matched by MapTemplateId (map 2's existing row is updated in place) and their depth bands replaced;
    /// the pool and spawn table are resolved by name. A config in the database with no file is left alone.
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
            else if (config.DepthBands.Count > 0)
            {
                config.DepthBands.Clear();
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
                MinDepth = b.MinDepth, MaxDepth = b.MaxDepth, MinLevel = b.MinLevel, MaxLevel = b.MaxLevel,
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

    private static async Task<T> ReadAsync<T>(string path, CancellationToken ct) =>
        JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json)
        ?? throw new InvalidDataException($"{path}: empty");
}
