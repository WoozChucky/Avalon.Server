using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>A map's depth bands (forest content pass) are owned by its procedural config and load with it.</summary>
public class ProceduralMapConfigRepositoryShould
{
    [Fact]
    public async Task Load_a_configs_depth_bands_with_it()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using (WorldDbContext write = database.CreateDbContext())
        {
            write.ProceduralMapConfigs.Add(new ProceduralMapConfig
            {
                MapTemplateId = new MapTemplateId(70),
                ChunkPoolId = new ChunkPoolId(1),
                SpawnTableId = new SpawnTableId(1),
                MainPathMin = 2,
                MainPathMax = 3,
                BackPortalTargetMapId = 1,
                DepthBands =
                [
                    new ProceduralDepthBand { MinDepth = 1, MaxDepth = 3, MinLevel = 1, MaxLevel = 3 },
                    new ProceduralDepthBand { MinDepth = 4, MaxDepth = null, MinLevel = 3, MaxLevel = 6 },
                ],
            });
            await write.SaveChangesAsync();
        }

        ProceduralMapConfig? config = await new ProceduralMapConfigRepository(database).FindByTemplateIdAsync(new MapTemplateId(70));

        Assert.NotNull(config);
        Assert.Equal(new (int, int?, ushort, ushort)[] { (1, 3, 1, 3), (4, null, 3, 6) },
            config!.DepthBands.OrderBy(b => b.MinDepth).Select(b => (b.MinDepth, b.MaxDepth, b.MinLevel, b.MaxLevel)));
    }

    /// <summary>MinDepth is part of the key but comes from the band file, so 0 is stored as 0, never generated.</summary>
    [Fact]
    public async Task Keep_a_band_that_starts_at_depth_zero()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using (WorldDbContext write = database.CreateDbContext())
        {
            write.ProceduralMapConfigs.Add(new ProceduralMapConfig
            {
                MapTemplateId = new MapTemplateId(71),
                ChunkPoolId = new ChunkPoolId(1),
                SpawnTableId = new SpawnTableId(1),
                MainPathMin = 2,
                MainPathMax = 3,
                BackPortalTargetMapId = 1,
                DepthBands = [new ProceduralDepthBand { MinDepth = 0, MaxDepth = 2, MinLevel = 1, MaxLevel = 2 }],
            });
            await write.SaveChangesAsync();
        }

        ProceduralMapConfig? config = await new ProceduralMapConfigRepository(database).FindByTemplateIdAsync(new MapTemplateId(71));

        Assert.Equal(0, Assert.Single(config!.DepthBands).MinDepth);
    }
}
