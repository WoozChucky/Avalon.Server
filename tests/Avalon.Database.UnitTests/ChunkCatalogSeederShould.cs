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
