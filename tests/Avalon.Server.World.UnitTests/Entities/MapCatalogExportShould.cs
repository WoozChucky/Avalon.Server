using System.Text.Json;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Exporter;
using Avalon.Server.World.UnitTests.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// The map catalog a client preloads from: every creature display id a map can show. Rendering is pure and tested
/// here; reading the rows is EF, checked against the committed file.
/// </summary>
public class MapCatalogExportShould
{
    private static CreatureTemplate Creature(ulong id, uint displayId) =>
        new() { Id = new CreatureTemplateId(id), DisplayId = displayId };

    private static MapCreatureSpawn Placed(int id, ushort map, ulong creature) => new()
    {
        Id = new MapCreatureSpawnId(id),
        MapTemplateId = new MapTemplateId(map),
        CreatureTemplateId = new CreatureTemplateId(creature),
    };

    /// <summary>
    /// A map lists its placed creatures and every creature its spawn table can roll, under any tag, once each and
    /// sorted; a creature with no display id adds nothing, and a map with neither is left out.
    /// </summary>
    [Fact]
    public void List_every_display_id_a_map_can_show()
    {
        CreatureTemplate[] creatures =
            [Creature(1, 3), Creature(2, 1), Creature(3, 0), Creature(4, 9), Creature(5, 8), Creature(6, 7)];
        MapCreatureSpawn[] placed = [Placed(1, 1, 1), Placed(2, 1, 2), Placed(3, 1, 1), Placed(4, 1, 3)];
        var table = new SpawnTable
        {
            Id = new SpawnTableId(1),
            Name = "forest",
            Entries =
            [
                new SpawnTableEntry { Tag = "pack", CreatureId = new CreatureTemplateId(4) },
                new SpawnTableEntry { Tag = "leader_pack", CreatureId = new CreatureTemplateId(5) },
                new SpawnTableEntry { Tag = "boss", CreatureId = new CreatureTemplateId(4) },
            ],
        };
        var forest = new ProceduralMapConfig { MapTemplateId = new MapTemplateId(2), SpawnTableId = new SpawnTableId(1) };
        var empty = new ProceduralMapConfig { MapTemplateId = new MapTemplateId(3), SpawnTableId = new SpawnTableId(9) };

        using var document = JsonDocument.Parse(MapCatalogExport.Render(creatures, placed, [forest, empty], [table]));
        JsonElement[] maps = document.RootElement.GetProperty("maps").EnumerateArray().ToArray();

        Assert.Equal([1, 2], maps.Select(m => m.GetProperty("mapId").GetInt32()));
        Assert.Equal([1u, 3u], maps[0].GetProperty("creatureDisplayIds").EnumerateArray().Select(e => e.GetUInt32()));
        Assert.Equal([8u, 9u], maps[1].GetProperty("creatureDisplayIds").EnumerateArray().Select(e => e.GetUInt32()));
    }

    /// <summary>
    /// The committed file is the seed rendered: a creature, spawn or spawn-table change without a re-export
    /// (map-catalog) fails here.
    /// </summary>
    [Fact]
    public async Task Match_the_committed_catalog()
    {
        using var database = SqliteDatabase.World();
        await using WorldDbContext context = database.CreateDbContext();
        await ChunkCatalogSeeder.SeedAsync(context, Path.Combine(RepositoryRoot(), "src", "Server", "Avalon.Server.World", "Maps"));

        string committed = File.ReadAllText(Path.Combine(RepositoryRoot(), "schema", MapCatalogExport.DirectoryName,
            MapCatalogExport.FileName));

        Assert.Equal(committed.Replace("\r\n", "\n", StringComparison.Ordinal),
            MapCatalogExport.Render(
                    context.CreatureTemplates.AsNoTracking().ToList(),
                    context.MapCreatureSpawns.AsNoTracking().ToList(),
                    context.ProceduralMapConfigs.AsNoTracking().ToList(),
                    context.SpawnTables.AsNoTracking().ToList())
                .Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}
