using System.Text.Json.Nodes;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// Migrations create the chunk pools and map configs but no chunk templates: those came only from
/// running Avalon.ChunkImporter against a local export, so a fresh database had no chunks, and the
/// migrations' pool memberships (looked up by name) matched nothing. The World server now seeds
/// the catalog committed under Maps/ on every start.
/// </summary>
public sealed class ChunkCatalogSeederShould : IDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task Seed_the_committed_catalog_into_an_empty_database()
    {
        using var database = SqliteDatabase.World();
        await using WorldDbContext db = database.CreateDbContext();

        await ChunkCatalogSeeder.SeedAsync(db, CommittedMapsRoot());

        Assert.Equal(38, await db.ChunkTemplates.CountAsync());
        ChunkTemplate path = await db.ChunkTemplates.SingleAsync(t => t.Name == "forest_path_01");
        Assert.Equal("Chunks/forest_path_01.obj", path.GeometryFile);
        Assert.Equal(["path", "forest"], path.Tags);
        Assert.Empty(path.DoodadSlots);
        await using WorldDbContext read = database.CreateDbContext();   // the slots as stored, not as tracked
        ChunkTemplate thicket = await read.ChunkTemplates.SingleAsync(t => t.Name == "forest_thicket_01");
        Assert.Equal(["doodad", "doodad", "doodad_large"], thicket.DoodadSlots.Select(s => s.Tag).Order(StringComparer.Ordinal));
        Assert.All(thicket.DoodadSlots, s => Assert.Equal(0f, s.LocalY));   // on the floor

        ChunkPool forest = await db.ChunkPools.Include(p => p.Memberships).SingleAsync(p => p.Name == "forest_pool");
        Assert.Equal(17, forest.Memberships.Count);
        Assert.Equal(3, await db.ChunkGroups.CountAsync());

        Assert.Equal(4, await db.MapChunkPlacements.CountAsync(p => p.MapTemplateId == new MapTemplateId(1)));
    }

    [Fact]
    public async Task Keep_ids_and_rows_when_run_again()
    {
        using var database = SqliteDatabase.World();
        string root = CommittedMapsRoot();

        await using (WorldDbContext first = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(first, root);
        Dictionary<string, int> idsBefore;
        await using (WorldDbContext read = database.CreateDbContext())
            idsBefore = await read.ChunkTemplates.ToDictionaryAsync(t => t.Name, t => t.Id.Value);

        await using (WorldDbContext second = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(second, root);

        await using WorldDbContext after = database.CreateDbContext();
        Assert.Equal(idsBefore, await after.ChunkTemplates.ToDictionaryAsync(t => t.Name, t => t.Id.Value));
        Assert.Equal(17, await after.Set<ChunkPoolMembership>().CountAsync());
        Assert.Equal(12, await after.Set<ChunkGroupMember>().CountAsync());
        Assert.Equal(4, await after.MapChunkPlacements.CountAsync());
    }

    [Fact]
    public async Task Update_a_changed_chunk_in_place()
    {
        using var database = SqliteDatabase.World();
        string root = CopyOfCommittedMaps();
        await using (WorldDbContext first = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(first, root);
        int id;
        await using (WorldDbContext read = database.CreateDbContext())
            id = (await read.ChunkTemplates.SingleAsync(t => t.Name == "forest_path_01")).Id.Value;

        string json = Path.Combine(root, "Chunks", "forest_path_01.json");
        File.WriteAllText(json, File.ReadAllText(json).Replace("chunks/forest_path_01", "chunks/forest_path_01_v2"));
        await using (WorldDbContext second = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(second, root);

        await using WorldDbContext after = database.CreateDbContext();
        ChunkTemplate updated = await after.ChunkTemplates.SingleAsync(t => t.Name == "forest_path_01");
        Assert.Equal(id, updated.Id.Value);
        Assert.Equal("chunks/forest_path_01_v2", updated.AssetKey);
    }

    [Fact]
    public async Task Refuse_a_layout_naming_an_unknown_chunk_and_write_nothing()
    {
        using var database = SqliteDatabase.World();
        string root = CopyOfCommittedMaps();
        string layout = Path.Combine(root, "TownLayouts", "1.json");
        File.WriteAllText(layout, File.ReadAllText(layout).Replace("\"town_se_01\"", "\"town_missing_01\""));

        await using (WorldDbContext db = database.CreateDbContext())
        {
            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
                () => ChunkCatalogSeeder.SeedAsync(db, root));
            Assert.Contains("1.json", error.Message);
            Assert.Contains("town_missing_01", error.Message);
        }

        await using WorldDbContext after = database.CreateDbContext();
        Assert.Equal(0, await after.ChunkTemplates.CountAsync());
    }

    [Fact]
    public async Task Refuse_a_chunk_without_its_geometry()
    {
        using var database = SqliteDatabase.World();
        string root = CopyOfCommittedMaps();
        File.Delete(Path.Combine(root, "Chunks", "forest_path_02.obj"));

        await using WorldDbContext db = database.CreateDbContext();
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => ChunkCatalogSeeder.SeedAsync(db, root));
        Assert.Contains("forest_path_02", error.Message);
    }

    /// <summary>Four 1x1 member chunks with exits only on their outer edges, written next to the committed catalog.</summary>
    private static void WriteGroupMembers(string root, string prefix, bool innerExit = false)
    {
        (string Suffix, string Exits)[] members =
        [
            ("sw", "\"N\": [" + (innerExit ? "\"center\"" : "") + "], \"E\": [], \"S\": [\"center\"], \"W\": []"),
            ("se", "\"N\": [], \"E\": [\"center\"], \"S\": [], \"W\": []"),
            ("nw", "\"N\": [], \"E\": [], \"S\": [], \"W\": []"),
            ("ne", "\"N\": [\"center\"], \"E\": [], \"S\": [], \"W\": []"),
        ];
        foreach ((string suffix, string exits) in members)
        {
            string name = $"{prefix}_{suffix}";
            File.WriteAllText(Path.Combine(root, "Chunks", name + ".json"),
                $$"""{ "name": "{{name}}", "assetKey": "chunks/{{name}}", "cellFootprintX": 1, "cellFootprintZ": 1, "cellSize": 30, "exits": { {{exits}} }, "spawnSlots": [], "portalSlots": [], "tags": ["test"] }""");
            File.WriteAllText(Path.Combine(root, "Chunks", name + ".obj"), "o Floor\nv 0 0 0\nv 30 0 0\nv 30 0 30\nf 1 2 3\n");
        }
    }

    /// <summary>
    /// Adds an empty "test_pool" to the copy's chunk-pools.json, then the group under <paramref name="pool" /> in its
    /// chunk-groups.json, keeping every group already there.
    /// </summary>
    private static void WriteGroup(string root, string pool, string group, string prefix, int neX = 1, int neZ = 1)
    {
        string poolsPath = Path.Combine(root, "chunk-pools.json");
        JsonObject pools = JsonNode.Parse(File.ReadAllText(poolsPath))!.AsObject();
        pools["test_pool"] = new JsonArray();
        File.WriteAllText(poolsPath, pools.ToJsonString());

        string groupsPath = Path.Combine(root, "chunk-groups.json");
        JsonObject groups = File.Exists(groupsPath) ? JsonNode.Parse(File.ReadAllText(groupsPath))!.AsObject() : new JsonObject();
        groups[pool] = JsonNode.Parse($$"""
            [ { "name": "{{group}}", "members": [
                { "chunk": "{{prefix}}_sw", "cellX": 0, "cellZ": 0 },
                { "chunk": "{{prefix}}_se", "cellX": 1, "cellZ": 0 },
                { "chunk": "{{prefix}}_nw", "cellX": 0, "cellZ": 1 },
                { "chunk": "{{prefix}}_ne", "cellX": {{neX}}, "cellZ": {{neZ}} } ] } ]
            """);
        File.WriteAllText(groupsPath, groups.ToJsonString());
    }

    /// <summary>
    /// A pool entry is a chunk's name (weight 1, no cap) or an object giving its weight and cap; the next start writes a
    /// changed entry onto the membership already there.
    /// </summary>
    [Fact]
    public async Task Store_each_pool_members_weight_and_cap_in_either_form()
    {
        string root = CopyOfCommittedMaps();
        using var database = SqliteDatabase.World();
        await using (WorldDbContext first = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(first, root);

        EditForestPool(root, "\"forest_rocks_01\"", """{ "chunk": "forest_rocks_01", "weight": 2.5, "max": 1 }""");
        EditForestPool(root, "\"forest_ruin_01\"", """{ "chunk": "forest_ruin_01", "weight": 0.5 }""");
        await using (WorldDbContext second = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(second, root);

        await using WorldDbContext read = database.CreateDbContext();
        Dictionary<string, (float, int?)> members = await read.Set<ChunkPoolMembership>()
            .Where(m => m.Pool.Name == "forest_pool")
            .Select(m => new { m.Template.Name, m.Weight, m.MaxPerLayout })
            .ToDictionaryAsync(m => m.Name, m => (m.Weight, m.MaxPerLayout), StringComparer.Ordinal);
        Assert.Equal((2.5f, 1), members["forest_rocks_01"]);
        Assert.Equal((0.5f, null), members["forest_ruin_01"]);
        Assert.Equal((1f, null), members["forest_path_01"]);
    }

    /// <summary>A weight of 0, below 0, NaN or infinite, a cap below 1, a key it does not know, and a chunk named twice in one pool.</summary>
    [Theory]
    [InlineData("""{ "chunk": "forest_rocks_01", "weight": 0 }""", "forest_rocks_01")]
    [InlineData("""{ "chunk": "forest_rocks_01", "weight": -1 }""", "forest_rocks_01")]
    [InlineData("""{ "chunk": "forest_rocks_01", "weight": "NaN" }""", "forest_rocks_01")]
    [InlineData("""{ "chunk": "forest_rocks_01", "weight": "Infinity" }""", "forest_rocks_01")]
    [InlineData("""{ "chunk": "forest_rocks_01", "max": 0 }""", "forest_rocks_01")]
    [InlineData("""{ "chunk": "forest_rocks_01", "weight": 2, "max": -1 }""", "forest_rocks_01")]
    [InlineData("""{ "chunk": "forest_rocks_01", "maxx": 1 }""", "forest_rocks_01")]   // a misspelt cap would leave it uncapped
    [InlineData("\"forest_path_02\"", "forest_path_02")]
    public async Task Refuse_a_bad_pool_member_naming_the_file_and_the_chunk(string entry, string chunk)
    {
        string root = CopyOfCommittedMaps();
        EditForestPool(root, "\"forest_rocks_01\"", entry);
        using var database = SqliteDatabase.World();
        await using WorldDbContext db = database.CreateDbContext();

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => ChunkCatalogSeeder.SeedAsync(db, root));
        Assert.Contains("chunk-pools.json", error.Message);
        Assert.Contains($"'{chunk}'", error.Message);
        Assert.Equal(0, await db.ChunkTemplates.CountAsync());
    }

    private static void EditForestPool(string root, string from, string to)
    {
        string path = Path.Combine(root, "chunk-pools.json");
        string text = File.ReadAllText(path);
        Assert.Contains(from, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(from, to, StringComparison.Ordinal));
    }

    /// <summary>tools/Avalon.ChunkGen validates its output with this reader, so it must work with no database.</summary>
    [Fact]
    public async Task Read_the_committed_catalog_without_a_database()
    {
        ChunkCatalogFiles files = await ChunkCatalogSeeder.ReadCatalogAsync(CommittedMapsRoot());

        Assert.Equal(38, files.Chunks.Count);
        Assert.Equal(3, files.Groups!["forest_pool"].Length);
        Assert.Contains("forest_pool", files.Pools.Keys);
        Assert.Single(files.Layouts);
    }

    [Fact]
    public async Task Seed_a_group_of_four_with_its_cells()
    {
        string root = CopyOfCommittedMaps();
        WriteGroupMembers(root, "g");
        WriteGroup(root, "test_pool", "test_group", "g");
        using var database = SqliteDatabase.World();
        await using (WorldDbContext db = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(db, root);

        await using WorldDbContext read = database.CreateDbContext();
        ChunkPool testPool = await read.ChunkPools.Include(p => p.Groups).ThenInclude(g => g.Members)
            .SingleAsync(p => p.Name == "test_pool");
        ChunkGroup group = Assert.Single(testPool.Groups);
        Assert.Equal("test_group", group.Name);
        Dictionary<int, string> names = await read.ChunkTemplates.ToDictionaryAsync(t => t.Id.Value, t => t.Name);
        Assert.Equal(["g_sw@0,0", "g_se@1,0", "g_nw@0,1", "g_ne@1,1"],
            group.Members.OrderBy(m => m.CellZ).ThenBy(m => m.CellX).Select(m => $"{names[m.ChunkTemplateId.Value]}@{m.CellX},{m.CellZ}"));
    }

    [Fact]
    public async Task Replace_the_groups_when_run_again()
    {
        string root = CopyOfCommittedMaps();
        WriteGroupMembers(root, "g");
        WriteGroup(root, "test_pool", "test_group", "g");
        using var database = SqliteDatabase.World();
        await using (WorldDbContext first = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(first, root);
        await using (WorldDbContext second = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(second, root);

        await using WorldDbContext read = database.CreateDbContext();
        ChunkGroup group = await read.ChunkGroups.Include(g => g.Members).SingleAsync(g => g.Name == "test_group");
        Assert.Equal(4, group.Members.Count);
    }

    [Theory]
    [InlineData("unknown_pool", "g", 1, 1, false)]   // a pool chunk-pools.json does not name
    [InlineData("test_pool", "nope", 1, 1, false)]   // members that do not exist
    [InlineData("test_pool", "g", 0, 0, false)]      // two members on one cell
    [InlineData("test_pool", "g", 2, 1, false)]      // cells that are not a full rectangle
    [InlineData("test_pool", "g", 1, 1, true)]       // an exit on an inner edge
    public async Task Refuse_a_bad_group_and_write_nothing(string pool, string prefix, int neX, int neZ, bool innerExit)
    {
        string root = CopyOfCommittedMaps();
        WriteGroupMembers(root, "g", innerExit);
        WriteGroup(root, pool, "test_group", prefix, neX, neZ);
        using var database = SqliteDatabase.World();
        await using WorldDbContext db = database.CreateDbContext();

        await Assert.ThrowsAsync<InvalidDataException>(() => ChunkCatalogSeeder.SeedAsync(db, root));
        Assert.Equal(0, await db.ChunkTemplates.CountAsync());
    }

    [Fact]
    public async Task Refuse_a_group_member_that_is_also_a_pool_member()
    {
        string root = CopyOfCommittedMaps();
        File.WriteAllText(Path.Combine(root, "chunk-groups.json"), """
            { "forest_pool": [ { "name": "bad", "members": [
                { "chunk": "forest_path_01", "cellX": 0, "cellZ": 0 },
                { "chunk": "forest_path_02", "cellX": 1, "cellZ": 0 } ] } ] }
            """);
        using var database = SqliteDatabase.World();
        await using WorldDbContext db = database.CreateDbContext();

        await Assert.ThrowsAsync<InvalidDataException>(() => ChunkCatalogSeeder.SeedAsync(db, root));
    }

    /// <summary>Adds an empty "test_pool" to the copy's chunk-pools.json and writes <paramref name="groupsJson" /> as its chunk-groups.json.</summary>
    private static void WriteGroupsFile(string root, string groupsJson)
    {
        string poolsPath = Path.Combine(root, "chunk-pools.json");
        JsonObject pools = JsonNode.Parse(File.ReadAllText(poolsPath))!.AsObject();
        pools["test_pool"] = new JsonArray();
        File.WriteAllText(poolsPath, pools.ToJsonString());
        File.WriteAllText(Path.Combine(root, "chunk-groups.json"), groupsJson);
    }

    private static async Task<InvalidDataException> RefuseAndWriteNothing(string root)
    {
        using var database = SqliteDatabase.World();
        await using WorldDbContext db = database.CreateDbContext();

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => ChunkCatalogSeeder.SeedAsync(db, root));
        Assert.Equal(0, await db.ChunkTemplates.CountAsync());
        Assert.Contains("chunk-groups.json", error.Message);
        return error;
    }

    public static TheoryData<string, string> BadGroups => new()
    {
        {
            """
            { "test_pool": [
                { "name": "twice", "members": [ { "chunk": "g_sw", "cellX": 0, "cellZ": 0 }, { "chunk": "g_se", "cellX": 1, "cellZ": 0 } ] },
                { "name": "twice", "members": [ { "chunk": "h_sw", "cellX": 0, "cellZ": 0 }, { "chunk": "h_se", "cellX": 1, "cellZ": 0 } ] } ] }
            """,
            "defined twice"
        },
        {
            """
            { "test_pool": [
                { "name": "first", "members": [ { "chunk": "g_sw", "cellX": 0, "cellZ": 0 }, { "chunk": "g_se", "cellX": 1, "cellZ": 0 } ] },
                { "name": "second", "members": [ { "chunk": "g_sw", "cellX": 0, "cellZ": 0 }, { "chunk": "h_se", "cellX": 1, "cellZ": 0 } ] } ] }
            """,
            "'g_sw' is in two groups"
        },
        {
            """{ "test_pool": [ { "name": "alone", "members": [ { "chunk": "g_sw", "cellX": 0, "cellZ": 0 } ] } ] }""",
            "at least two members"
        },
        {
            """
            { "test_pool": [ { "name": "negative", "members": [
                { "chunk": "g_sw", "cellX": -1, "cellZ": 0 }, { "chunk": "g_se", "cellX": 0, "cellZ": 0 } ] } ] }
            """,
            "'g_sw' has a negative cell"
        },
        { """{ "test_pool": [ { "name": "empty" } ] }""", "'empty'" },
    };

    /// <summary>A group defined twice, a chunk in two groups, a group of one, a negative cell, a group without members.</summary>
    [Theory]
    [MemberData(nameof(BadGroups))]
    public async Task Refuse_a_bad_group_naming_the_file_and_the_fault(string groupsJson, string fault)
    {
        string root = CopyOfCommittedMaps();
        WriteGroupMembers(root, "g");
        WriteGroupMembers(root, "h");
        WriteGroupsFile(root, groupsJson);

        InvalidDataException error = await RefuseAndWriteNothing(root);
        Assert.Contains(fault, error.Message);
    }

    [Fact]
    public async Task Refuse_a_member_that_is_not_1x1()
    {
        string root = CopyOfCommittedMaps();
        WriteGroupMembers(root, "g");
        File.WriteAllText(Path.Combine(root, "Chunks", "g_wide.json"),
            """{ "name": "g_wide", "assetKey": "chunks/g_wide", "cellFootprintX": 2, "cellFootprintZ": 1, "cellSize": 30, "exits": { "N": [], "E": [], "S": [], "W": [] }, "spawnSlots": [], "portalSlots": [], "tags": ["test"] }""");
        File.WriteAllText(Path.Combine(root, "Chunks", "g_wide.obj"), "o Floor\nv 0 0 0\nv 60 0 0\nv 60 0 30\nf 1 2 3\n");
        WriteGroupsFile(root, """
            { "test_pool": [ { "name": "wide", "members": [
                { "chunk": "g_sw", "cellX": 0, "cellZ": 0 }, { "chunk": "g_wide", "cellX": 1, "cellZ": 0 } ] } ] }
            """);

        InvalidDataException error = await RefuseAndWriteNothing(root);
        Assert.Contains("'g_wide' is not 1x1", error.Message);
    }

    [Fact]
    public async Task Leave_the_groups_alone_when_chunk_groups_json_is_missing()
    {
        string root = CopyOfCommittedMaps();
        WriteGroupMembers(root, "g");
        WriteGroup(root, "test_pool", "test_group", "g");
        using var database = SqliteDatabase.World();
        await using (WorldDbContext first = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(first, root);

        DropTheSetPieces(root);
        ChunkCatalogSeedResult second;
        await using (WorldDbContext db = database.CreateDbContext())
            second = await ChunkCatalogSeeder.SeedAsync(db, root);

        Assert.Equal(0, second.GroupsReplaced);
        await using WorldDbContext read = database.CreateDbContext();
        ChunkGroup group = await read.ChunkGroups.Include(g => g.Members).SingleAsync(g => g.Name == "test_group");
        Assert.Equal(4, group.Members.Count);
    }

    [Fact]
    public async Task Seed_the_forests_spawn_table_and_config_into_an_empty_database()
    {
        using var database = SqliteDatabase.World();
        await using (WorldDbContext db = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(db, CommittedMapsRoot());

        await using WorldDbContext read = database.CreateDbContext();
        SpawnTable table = await read.SpawnTables.SingleAsync(t => t.Name == "forest_creatures");
        Assert.Equal(1, table.Id.Value);
        Assert.Equal(9, table.Entries.Count);
        ProceduralMapConfig config = await read.ProceduralMapConfigs.SingleAsync(c => c.MapTemplateId == new MapTemplateId(2));
        ChunkPool pool = await read.ChunkPools.SingleAsync(p => p.Name == "forest_pool");
        Assert.Equal((pool.Id.Value, table.Id.Value), (config.ChunkPoolId.Value, config.SpawnTableId.Value));
        Assert.Equal(8, config.MinSetPieceStep);
    }

    /// <summary>A map file without minSetPieceStep places set pieces from step 1, as before the limit existed.</summary>
    [Fact]
    public async Task Seed_no_first_set_piece_step_when_the_file_names_none()
    {
        string root = CopyOfCommittedMaps();
        string path = Path.Combine(root, "ProceduralMaps", "2.json");
        JsonObject map = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(map.Remove("minSetPieceStep"));
        File.WriteAllText(path, map.ToJsonString());
        using var database = SqliteDatabase.World();
        await using (WorldDbContext db = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(db, root);

        await using WorldDbContext read = database.CreateDbContext();
        ProceduralMapConfig config = await read.ProceduralMapConfigs.SingleAsync(c => c.MapTemplateId == new MapTemplateId(2));
        Assert.Equal(0, config.MinSetPieceStep);
    }

    /// <summary>Owner decision: the rows the old migration SQL wrote are adopted, ids kept, values replaced from the files.</summary>
    [Fact]
    public async Task Adopt_the_existing_spawn_table_and_config_keeping_their_ids()
    {
        using var database = SqliteDatabase.World();
        await using (WorldDbContext before = database.CreateDbContext())
        {
            before.SpawnTables.Add(new SpawnTable
            {
                Id = new SpawnTableId(1),
                Name = "forest_creatures",
                Entries = [new SpawnTableEntry { Tag = "pack", CreatureId = new CreatureTemplateId(2), Weight = 1f, MinCount = 1, MaxCount = 2 }],
            });
            before.ProceduralMapConfigs.Add(new ProceduralMapConfig
            {
                MapTemplateId = new MapTemplateId(2),
                ChunkPoolId = new ChunkPoolId(1),
                SpawnTableId = new SpawnTableId(1),
                MainPathMin = 2,
                MainPathMax = 3,
                HasBoss = true,
                BackPortalTargetMapId = 1,
            });
            await before.SaveChangesAsync();
        }

        await using (WorldDbContext first = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(first, CommittedMapsRoot());
        await using (WorldDbContext second = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(second, CommittedMapsRoot());

        await using WorldDbContext read = database.CreateDbContext();
        SpawnTable table = Assert.Single(await read.SpawnTables.ToListAsync());
        Assert.Equal(1, table.Id.Value);
        Assert.Equal(9, table.Entries.Count);
        Assert.DoesNotContain(table.Entries, e => e.CreatureId.Value == 2);
        ProceduralMapConfig config = Assert.Single(await read.ProceduralMapConfigs.ToListAsync());
        Assert.Equal(((ushort)12, (ushort)16), (config.MainPathMin, config.MainPathMax));
        Assert.Equal(3, config.DepthBands.Count);
        Assert.Equal(1, config.SpawnTableId.Value);
    }

    [Fact]
    public async Task Write_a_maps_depth_bands_and_doodad_rules_and_replace_them_on_the_next_start()
    {
        string root = CopyOfCommittedMaps();
        File.WriteAllText(Path.Combine(root, "ProceduralMaps", "2.json"), """
            { "mapTemplateId": 2, "chunkPool": "forest_pool", "spawnTable": "forest_creatures",
              "mainPathMin": 4, "mainPathMax": 7, "branchChance": 0.4, "branchMaxDepth": 2, "hasBoss": true,
              "backPortalTargetMapId": 1, "forwardPortalTargetMapId": null,
              "depthBands": [ { "minDepth": 1, "maxDepth": 3, "minLevel": 1, "maxLevel": 3 },
                              { "minDepth": 4, "maxDepth": null, "minLevel": 3, "maxLevel": 6 } ],
              "doodadRules": [
                { "doodadTemplateId": 902, "slotTag": "doodad_large", "min": 0, "max": 2, "required": false, "minSpacing": 40 },
                { "doodadTemplateId": 901, "slotTag": "doodad", "min": 1, "max": 1, "required": true, "minSpacing": 0,
                  "minDepth": 8, "visibleGate": { "questId": 7, "condition": "onObjective", "objectiveId": 701 } } ] }
            """);
        using var database = SqliteDatabase.World();
        await AddDoodadTemplatesAsync(database);
        await using (WorldDbContext first = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(first, root);
        await using (WorldDbContext second = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(second, root);

        await using WorldDbContext read = database.CreateDbContext();
        ProceduralMapConfig config = await read.ProceduralMapConfigs.SingleAsync(c => c.MapTemplateId == new MapTemplateId(2));
        Assert.Equal([1, 4], config.DepthBands.Select(b => b.MinDepth).Order());
        var rules = config.DoodadRules.OrderBy(r => r.Sequence).ToList();
        Assert.Equal([(1, 902u, "doodad_large", false, 40f), (2, 901u, "doodad", true, 0f)],
            rules.Select(r => (r.Sequence, r.DoodadTemplateId.Value, r.SlotTag, r.Required, r.MinSpacing)));
        Assert.Equal((8, (int?)null, 7u, DoodadGateCondition.OnObjective, 701u),
            (rules[1].MinDepth!.Value, rules[1].MaxDepth, rules[1].VisibleQuestId!.Value, rules[1].VisibleCondition!.Value,
                rules[1].VisibleObjectiveId!.Value));
        Assert.Null(rules[0].VisibleQuestId);
    }

    /// <summary>
    /// A doodad rule the world could not honour is refused, naming the file: a template that does not exist, a tag no
    /// doodad slot of the pool carries, a count range reversed or above 50, a spacing that is not a finite 0 or more, a
    /// reversed or negative depth range, a gate condition that is no condition, a solid template at a slot other than
    /// doodad_large, a required rule on a solid template or wanting more doodads than the pool has slots, and a gate on a
    /// solid template.
    /// </summary>
    [Theory]
    [InlineData("unknown template", "doodad template 99, which does not exist")]
    [InlineData("tag no slot carries", "'nowhere', which no doodad slot")]
    [InlineData("min above max", "count 3-2")]
    [InlineData("max above 50", "count 0-51")]
    [InlineData("spacing NaN", "minSpacing NaN")]
    [InlineData("spacing negative", "minSpacing -1")]
    [InlineData("depths reversed", "minDepth 5 above maxDepth 2")]
    [InlineData("min depth negative", "minDepth -1")]
    [InlineData("max depth negative", "maxDepth -2")]
    [InlineData("unknown condition", "condition 'sometimes'")]
    [InlineData("solid template at a small slot", "which is solid, at 'doodad' slots")]
    [InlineData("required solid template", "names doodad template 902, which is solid")]
    [InlineData("required beyond the slots", "has only")]
    [InlineData("gate on solid template", "which is solid and never hidden")]
    public async Task Refuse_a_doodad_rule_the_catalog_cannot_honour(string row, string fault)
    {
        string rule = row switch
        {
            "unknown template" => Rule(99),
            "tag no slot carries" => Rule(901, tag: "nowhere"),
            "min above max" => Rule(901, min: 3, max: 2),
            "max above 50" => Rule(901, max: 51),
            "spacing NaN" => Rule(901, spacing: "\"NaN\""),
            "spacing negative" => Rule(901, spacing: "-1"),
            "depths reversed" => Rule(901, extra: "\"minDepth\": 5, \"maxDepth\": 2"),
            "min depth negative" => Rule(901, extra: "\"minDepth\": -1"),
            "max depth negative" => Rule(901, extra: "\"maxDepth\": -2"),
            "unknown condition" => Rule(901, extra: "\"usableGate\": { \"questId\": 7, \"condition\": \"sometimes\" }"),
            "solid template at a small slot" => Rule(902),
            "required solid template" => Rule(902, tag: "doodad_large", required: true),
            "required beyond the slots" => Rule(901, max: 50, required: true),
            _ => Rule(902, tag: "doodad_large", extra: "\"visibleGate\": { \"questId\": 7, \"condition\": \"completed\" }"),
        };
        string root = CopyOfCommittedMaps();
        string path = Path.Combine(root, "ProceduralMaps", "2.json");
        JsonObject map = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        map["doodadRules"] = JsonNode.Parse($"[ {rule} ]");
        File.WriteAllText(path, map.ToJsonString());
        using var database = SqliteDatabase.World();
        await AddDoodadTemplatesAsync(database);
        await using WorldDbContext db = database.CreateDbContext();

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => ChunkCatalogSeeder.SeedAsync(db, root));
        Assert.StartsWith(path + ":", error.Message, StringComparison.Ordinal);
        Assert.Contains(fault, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.ChunkTemplates.CountAsync());
        Assert.Equal(0, await db.ProceduralMapConfigs.CountAsync());
    }

    private static string Rule(uint template, string tag = "doodad", int min = 0, int max = 1, bool required = false,
        string spacing = "0", string? extra = null) =>
        $$"""{ "doodadTemplateId": {{template}}, "slotTag": "{{tag}}", "min": {{min}}, "max": {{max}}, "required": {{(required ? "true" : "false")}}, "minSpacing": {{spacing}}{{(extra is null ? "" : ", " + extra)}} }""";

    /// <summary>
    /// Doodad template 901, a walk-through crate, and 902, a solid shrine: what the rules above name, beside the seeded
    /// forest doodads the committed 2.json names.
    /// </summary>
    private static async Task AddDoodadTemplatesAsync(SqliteDatabase<WorldDbContext> database)
    {
        await using WorldDbContext db = database.CreateDbContext();
        db.DoodadTemplates.AddRange(DoodadRow(901, "Crate", DoodadCollision.None), DoodadRow(902, "Shrine", DoodadCollision.Box));
        await db.SaveChangesAsync();
    }

    private static DoodadTemplate DoodadRow(uint id, string name, DoodadCollision collision) => new()
    {
        Id = new DoodadTemplateId(id),
        Name = name,
        ModelKit = "props-kit",
        ModelKey = name,
        Interaction = DoodadInteraction.Click,
        InteractionRange = 5f,
        UseLimit = DoodadUseLimit.OncePerInstance,
        SpentLook = DoodadSpentLook.ShowUsed,
        Collision = collision,
        CollisionHalfX = collision == DoodadCollision.None ? 0f : 0.6f,
        CollisionHalfZ = collision == DoodadCollision.None ? 0f : 0.6f,
        CollisionHeight = collision == DoodadCollision.None ? 0f : 1.4f,
    };

    /// <summary>Review Focus 4: every bad edit refuses the whole catalog before anything is written.</summary>
    [Theory]
    [InlineData("ProceduralMaps/2.json", "\"chunkPool\": \"forest_pool\"", "\"chunkPool\": \"no_such_pool\"")]
    [InlineData("ProceduralMaps/2.json", "\"spawnTable\": \"forest_creatures\"", "\"spawnTable\": \"no_such_table\"")]
    [InlineData("ProceduralMaps/2.json", "\"mapTemplateId\": 2", "\"mapTemplateId\": 3")]                // the file is named 2.json
    [InlineData("spawn-tables.json", "\"creatureId\": 10,", "\"creatureId\": 999,")]                      // no such creature
    [InlineData("spawn-tables.json", "\"tag\": \"rare\"", "\"tag\": \"elite\"")]                           // no slot uses it, and 'rare' slots lose their entries
    public async Task Refuse_bad_procedural_data_and_write_nothing(string file, string from, string to)
    {
        string root = CopyOfCommittedMaps();
        string path = Path.Combine(root, file);
        string text = File.ReadAllText(path);
        Assert.Contains(from, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(from, to, StringComparison.Ordinal));
        using var database = SqliteDatabase.World();
        await using WorldDbContext db = database.CreateDbContext();

        await Assert.ThrowsAsync<InvalidDataException>(() => ChunkCatalogSeeder.SeedAsync(db, root));
        Assert.Equal(0, await db.ChunkTemplates.CountAsync());
        Assert.Equal(0, await db.SpawnTables.CountAsync());
    }

    [Theory]
    [InlineData(99)]   // no MapTemplate 99
    [InlineData(1)]    // map 1 is the town
    public async Task Refuse_a_procedural_map_that_is_not_a_normal_map(int mapId)
    {
        string root = CopyOfCommittedMaps();
        string source = File.ReadAllText(Path.Combine(root, "ProceduralMaps", "2.json"));
        File.WriteAllText(Path.Combine(root, "ProceduralMaps", $"{mapId}.json"),
            source.Replace("\"mapTemplateId\": 2", $"\"mapTemplateId\": {mapId}", StringComparison.Ordinal));
        using var database = SqliteDatabase.World();
        await using WorldDbContext db = database.CreateDbContext();

        await Assert.ThrowsAsync<InvalidDataException>(() => ChunkCatalogSeeder.SeedAsync(db, root));
        Assert.Equal(0, await db.ProceduralMapConfigs.CountAsync());
    }

    /// <summary>Owner rule: every slot tag but entry and empty needs a spawn-table entry (the reverse of the tag check).</summary>
    [Fact]
    public async Task Refuse_a_slot_tag_the_spawn_table_has_no_entry_for()
    {
        string root = CopyOfCommittedMaps();
        EditSpawnTable(root, entries =>
        {
            foreach (JsonNode? rare in entries.Where(e => (string?)e!["tag"] == "rare").ToList())
                entries.Remove(rare);
        });

        InvalidDataException error = await RefuseProceduralDataAndWriteNothing(root);
        Assert.Contains("'rare' slots", error.Message);
    }

    /// <summary>
    /// A doodad slot with a blank tag, a yaw that is not a quarter turn, a position outside the chunk's own cell (its
    /// edges included) or a height that is not finite is refused, naming the file and the fault.
    /// </summary>
    [Theory]
    [InlineData("doodad", 5f, "0", 5f, 45, "has yaw 45")]
    [InlineData("doodad", 5f, "0", 5f, 360, "has yaw 360")]
    [InlineData("doodad", 5f, "0", 5f, -90, "has yaw -90")]
    [InlineData(" ", 5f, "0", 5f, 90, "has no tag")]
    [InlineData("doodad", -1f, "0", 5f, 90, "lies outside the chunk")]
    [InlineData("doodad", 5f, "0", 30.5f, 90, "lies outside the chunk")]
    [InlineData("doodad", 5f, "\"NaN\"", 5f, 90, "has localY NaN")]
    [InlineData("doodad", 5f, "\"-Infinity\"", 5f, 90, "has localY -Infinity")]
    public async Task Refuse_a_bad_doodad_slot_naming_the_file_and_the_fault(string tag, float x, string y, float z, int yaw, string fault)
    {
        string root = CopyOfCommittedMaps();
        string path = WriteDoodadSlot(root, tag, x, y, z, yaw);

        InvalidDataException error = await RefuseProceduralDataAndWriteNothing(root);
        Assert.StartsWith(path + ":", error.Message, StringComparison.Ordinal);
        Assert.Contains(fault, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Gives the copy's forest_path_01 (a hand-made chunk with no doodad slots) one doodad slot; returns its file.</summary>
    private static string WriteDoodadSlot(string root, string tag, float x, string y, float z, int yaw)
    {
        string path = Path.Combine(root, "Chunks", "forest_path_01.json");
        JsonObject chunk = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        chunk["doodadSlots"] = new JsonArray(new JsonObject
        {
            ["tag"] = tag,
            ["localX"] = x,
            ["localY"] = JsonNode.Parse(y),
            ["localZ"] = z,
            ["yaw"] = yaw,
        });
        File.WriteAllText(path, chunk.ToJsonString());
        return path;
    }

    /// <summary>Owner rule: a leader_pack entry is rolled at a leader slot, so it is refused when the pool has none.</summary>
    [Fact]
    public async Task Refuse_a_leader_pack_entry_when_the_pool_has_no_leader_slot()
    {
        string root = CopyOfCommittedMaps();
        DropTheSetPieces(root);
        EditSpawnTable(root, entries => entries.Add(SpawnEntry("leader_pack", 5, 2, 3)));

        InvalidDataException error = await RefuseProceduralDataAndWriteNothing(root);
        Assert.Contains("'leader_pack'", error.Message);
    }

    [Fact]
    public async Task Accept_a_leader_pack_entry_when_the_pool_has_a_leader_slot()
    {
        string root = CopyOfCommittedMaps();
        DropTheSetPieces(root);   // the leader slot below is the pool's only one
        string chunkPath = Path.Combine(root, "Chunks", "forest_path_01.json");
        JsonObject chunk = JsonNode.Parse(File.ReadAllText(chunkPath))!.AsObject();
        chunk["spawnSlots"]!.AsArray().Add(JsonNode.Parse("""{ "tag": "leader", "localX": 15, "localY": 1, "localZ": 20 }"""));
        File.WriteAllText(chunkPath, chunk.ToJsonString());
        EditSpawnTable(root, entries =>
        {
            entries.Add(SpawnEntry("leader", 8, 1, 1));
            entries.Add(SpawnEntry("leader_pack", 5, 2, 3));
        });
        using var database = SqliteDatabase.World();
        await using (WorldDbContext db = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(db, root);

        await using WorldDbContext read = database.CreateDbContext();
        SpawnTable table = await read.SpawnTables.SingleAsync(t => t.Name == "forest_creatures");
        Assert.Equal(8, table.Entries.Count);   // six from the table without the set pieces, plus leader and leader_pack
        Assert.Single(table.Entries, e => e.Tag == "leader_pack");
    }

    [Theory]
    [InlineData("chunkPool")]
    [InlineData("spawnTable")]
    public async Task Refuse_a_procedural_map_missing_a_name_and_name_the_file(string property)
    {
        string root = CopyOfCommittedMaps();
        string path = Path.Combine(root, "ProceduralMaps", "2.json");
        JsonObject map = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(map.Remove(property));
        File.WriteAllText(path, map.ToJsonString());

        InvalidDataException error = await RefuseProceduralDataAndWriteNothing(root);
        Assert.Contains(path, error.Message, StringComparison.Ordinal);
        Assert.Contains(property, error.Message, StringComparison.Ordinal);
    }

    /// <summary>A field the file leaves out would otherwise be read as 0 or false (a back portal to map 0, say).</summary>
    [Theory]
    [InlineData("mapTemplateId")]
    [InlineData("mainPathMin")]
    [InlineData("mainPathMax")]
    [InlineData("branchChance")]
    [InlineData("branchMaxDepth")]
    [InlineData("hasBoss")]
    [InlineData("backPortalTargetMapId")]
    public async Task Refuse_a_procedural_map_missing_a_required_field_and_name_the_file(string property)
    {
        string root = CopyOfCommittedMaps();
        string path = Path.Combine(root, "ProceduralMaps", "2.json");
        JsonObject map = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(map.Remove(property));
        File.WriteAllText(path, map.ToJsonString());

        InvalidDataException error = await RefuseProceduralDataAndWriteNothing(root);
        Assert.StartsWith(path + ":", error.Message, StringComparison.Ordinal);
        Assert.Contains(property, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("backPortalTargetMapId")]
    [InlineData("forwardPortalTargetMapId")]
    public async Task Refuse_a_portal_to_a_map_that_does_not_exist_and_name_the_file(string property)
    {
        string root = CopyOfCommittedMaps();
        string path = Path.Combine(root, "ProceduralMaps", "2.json");
        JsonObject map = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        map[property] = 99;
        File.WriteAllText(path, map.ToJsonString());

        InvalidDataException error = await RefuseProceduralDataAndWriteNothing(root);
        Assert.StartsWith(path + ":", error.Message, StringComparison.Ordinal);
        Assert.Contains(property, error.Message, StringComparison.Ordinal);
        Assert.Contains("99", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Task 8 ruling: a band above the highest CreatureBaseStats level would spawn creatures that report its level with
    /// the highest row's stats (base stats are seeded to level 10).
    /// </summary>
    [Fact]
    public async Task Refuse_a_depth_band_above_the_highest_creature_base_stats_level_and_name_the_file_and_band()
    {
        string root = CopyOfCommittedMaps();
        string path = Path.Combine(root, "ProceduralMaps", "2.json");
        JsonObject map = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonNode top = map["depthBands"]!.AsArray().Single(b => (int)b!["minDepth"]! == 8)!;
        top["maxLevel"] = 11;
        File.WriteAllText(path, map.ToJsonString());

        InvalidDataException error = await RefuseProceduralDataAndWriteNothing(root);
        Assert.StartsWith(path + ":", error.Message, StringComparison.Ordinal);
        Assert.Contains("depth 8", error.Message, StringComparison.Ordinal);
        Assert.Contains("11", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accept_a_depth_band_that_tops_out_at_the_highest_creature_base_stats_level()
    {
        string root = CopyOfCommittedMaps();
        string path = Path.Combine(root, "ProceduralMaps", "2.json");
        JsonObject map = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        map["depthBands"]!.AsArray().Single(b => (int)b!["minDepth"]! == 8)!["maxLevel"] = 10;
        File.WriteAllText(path, map.ToJsonString());
        using var database = SqliteDatabase.World();
        await using (WorldDbContext db = database.CreateDbContext())
            await ChunkCatalogSeeder.SeedAsync(db, root);

        await using WorldDbContext read = database.CreateDbContext();
        ProceduralMapConfig config = await read.ProceduralMapConfigs.SingleAsync(c => c.MapTemplateId == new MapTemplateId(2));
        Assert.Equal(10, config.DepthBands.Max(b => b.MaxLevel));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public async Task Refuse_a_group_without_a_name_and_name_the_file(string? name)
    {
        string root = CopyOfCommittedMaps();
        string path = Path.Combine(root, "chunk-groups.json");
        JsonObject groups = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        groups["forest_pool"]!.AsArray()[0]!["name"] = name;
        File.WriteAllText(path, groups.ToJsonString());

        InvalidDataException error = await RefuseProceduralDataAndWriteNothing(root);
        Assert.StartsWith(path + ":", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Name_the_spawn_tables_file_when_a_creature_does_not_exist()
    {
        string root = CopyOfCommittedMaps();
        EditSpawnTable(root, entries => entries.Add(SpawnEntry("pack", 999, 1, 1)));

        InvalidDataException error = await RefuseProceduralDataAndWriteNothing(root);
        Assert.StartsWith(Path.Combine(root, "spawn-tables.json") + ":", error.Message, StringComparison.Ordinal);
        Assert.Contains("999", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Rewrites the copy's forest_creatures table through <paramref name="edit" />.</summary>
    private static void EditSpawnTable(string root, Action<JsonArray> edit)
    {
        string path = Path.Combine(root, "spawn-tables.json");
        JsonObject tables = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(tables["forest_creatures"]!.AsArray());
        File.WriteAllText(path, tables.ToJsonString());
    }

    /// <summary>
    /// Removes the copy's chunk-groups.json and the spawn-table entries only the set pieces need (boss, leader and its
    /// leader_pack), so the copy's pool is its single pieces alone and its catalog still passes the tag check.
    /// </summary>
    private static void DropTheSetPieces(string root)
    {
        File.Delete(Path.Combine(root, "chunk-groups.json"));
        EditSpawnTable(root, entries =>
        {
            foreach (JsonNode? entry in entries.Where(e => (string?)e!["tag"] is "boss" or "leader" or "leader_pack").ToList())
                entries.Remove(entry);
        });
    }

    private static JsonNode SpawnEntry(string tag, int creatureId, int min, int max) =>
        JsonNode.Parse($$"""{ "tag": "{{tag}}", "creatureId": {{creatureId}}, "weight": 1.0, "min": {{min}}, "max": {{max}} }""")!;

    private static async Task<InvalidDataException> RefuseProceduralDataAndWriteNothing(string root)
    {
        using var database = SqliteDatabase.World();
        await using WorldDbContext db = database.CreateDbContext();

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => ChunkCatalogSeeder.SeedAsync(db, root));
        Assert.Equal(0, await db.ChunkTemplates.CountAsync());
        Assert.Equal(0, await db.SpawnTables.CountAsync());
        Assert.Equal(0, await db.ProceduralMapConfigs.CountAsync());
        return error;
    }

    private static string CommittedMapsRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string maps = Path.Combine(dir.FullName, "src", "Server", "Avalon.Server.World", "Maps");
            if (Directory.Exists(maps)) return maps;
        }
        throw new DirectoryNotFoundException("src/Server/Avalon.Server.World/Maps not found above the test output");
    }

    private string CopyOfCommittedMaps()
    {
        string target = Path.Combine(Path.GetTempPath(), $"avalon-maps-{Guid.NewGuid():N}");
        _tempDirs.Add(target);
        string source = CommittedMapsRoot();
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        return target;
    }

    public void Dispose()
    {
        foreach (string dir in _tempDirs)
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
