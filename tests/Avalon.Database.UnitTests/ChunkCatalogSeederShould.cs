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
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext db = database.CreateDbContext();

        await ChunkCatalogSeeder.SeedAsync(db, CommittedMapsRoot());

        Assert.Equal(18, await db.ChunkTemplates.CountAsync());
        ChunkTemplate path = await db.ChunkTemplates.SingleAsync(t => t.Name == "forest_path_01");
        Assert.Equal("Chunks/forest_path_01.obj", path.GeometryFile);
        Assert.Equal(["path", "forest"], path.Tags);

        ChunkPool forest = await db.ChunkPools.Include(p => p.Memberships).SingleAsync(p => p.Name == "forest_pool");
        Assert.Equal(10, forest.Memberships.Count);

        Assert.Equal(4, await db.MapChunkPlacements.CountAsync(p => p.MapTemplateId == new MapTemplateId(1)));
    }

    [Fact]
    public async Task Keep_ids_and_rows_when_run_again()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
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
        Assert.Equal(10, await after.Set<ChunkPoolMembership>().CountAsync());
        Assert.Equal(4, await after.MapChunkPlacements.CountAsync());
    }

    [Fact]
    public async Task Update_a_changed_chunk_in_place()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
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
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
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
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
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

    /// <summary>The generator tool (Task 9) validates its output with this reader, so it must work with no database.</summary>
    [Fact]
    public async Task Read_the_committed_catalog_without_a_database()
    {
        ChunkCatalogFiles files = await ChunkCatalogSeeder.ReadCatalogAsync(CommittedMapsRoot());

        Assert.Equal(18, files.Chunks.Count);
        Assert.Contains("forest_pool", files.Pools.Keys);
        Assert.Single(files.Layouts);
    }

    [Fact]
    public async Task Seed_a_group_of_four_with_its_cells()
    {
        string root = CopyOfCommittedMaps();
        WriteGroupMembers(root, "g");
        WriteGroup(root, "test_pool", "test_group", "g");
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
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
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
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
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
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
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext db = database.CreateDbContext();

        await Assert.ThrowsAsync<InvalidDataException>(() => ChunkCatalogSeeder.SeedAsync(db, root));
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
