using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Xunit;

namespace Avalon.Database.UnitTests;

public class MapCreatureSpawnRepositoryShould
{
    /// <summary>
    /// #421: placement turns a spawn's path into the creature's patrol route, and it reads spawns
    /// through this repository once per instance build — so the path and its points have to come
    /// back loaded, not as an unloaded navigation property. Run against the real EF model.
    /// </summary>
    [Fact]
    public async Task Load_A_Spawns_Path_With_Its_Points()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();

        await using (WorldDbContext write = database.CreateDbContext())
        {
            write.CreaturePaths.Add(new CreaturePath
            {
                Id = new CreaturePathId(1),
                Name = "town wall",
                Points =
                [
                    new CreaturePathPoint { Sequence = 1, OffsetX = 1f, OffsetZ = 0f, WaitMs = 1500 },
                    new CreaturePathPoint { Sequence = 2, OffsetX = 0f, OffsetZ = 5f, WaitMs = 0 },
                ]
            });
            await write.SaveChangesAsync();

            MapCreatureSpawn uriel = write.MapCreatureSpawns.Single(s => s.Id == new MapCreatureSpawnId(1));
            uriel.PathId = new CreaturePathId(1);
            await write.SaveChangesAsync();
        }

        IReadOnlyCollection<MapCreatureSpawn> spawns =
            await new MapCreatureSpawnRepository(database).FindByMapAsync(new MapTemplateId(1));

        MapCreatureSpawn withPath = spawns.Single(s => s.Id == new MapCreatureSpawnId(1));
        Assert.NotNull(withPath.Path);
        Assert.Equal([1500, 0], withPath.Path!.Points.OrderBy(p => p.Sequence).Select(p => p.WaitMs));

        // The seeded town spawns name no path, and must still load.
        Assert.All(spawns.Where(s => s.Id != new MapCreatureSpawnId(1)), s => Assert.Null(s.Path));
    }
}
