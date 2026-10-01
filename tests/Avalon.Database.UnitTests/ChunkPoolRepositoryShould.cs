using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// A pool loads with its memberships and its set pieces' members. Two collections in one query made EF warn about a
/// cartesian product on every World start, every API layout preview and every observability cache miss; the warning is
/// an error here, so the query must say how it splits.
/// </summary>
public class ChunkPoolRepositoryShould
{
    [Fact]
    public async Task Load_a_pools_memberships_and_set_pieces_without_a_multiple_collection_warning()
    {
        using var database = new SqliteDatabase<WorldDbContext>(options => new WorldDbContext(options),
            builder => builder.ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning)));
        await using (WorldDbContext write = database.CreateDbContext())
        {
            write.ChunkTemplates.AddRange(
                new ChunkTemplate { Id = new ChunkTemplateId(901), Name = "a", AssetKey = "a", GeometryFile = "a.obj" },
                new ChunkTemplate { Id = new ChunkTemplateId(902), Name = "b", AssetKey = "b", GeometryFile = "b.obj" },
                new ChunkTemplate { Id = new ChunkTemplateId(903), Name = "c", AssetKey = "c", GeometryFile = "c.obj" });
            write.ChunkPools.Add(new ChunkPool
            {
                Id = new ChunkPoolId(90), Name = "test_pool",
                Memberships = [new ChunkPoolMembership { ChunkPoolId = new ChunkPoolId(90), ChunkTemplateId = new ChunkTemplateId(901), Weight = 1f }],
                Groups =
                [
                    new ChunkGroup
                    {
                        Name = "pair", ChunkPoolId = new ChunkPoolId(90),
                        Members =
                        [
                            new ChunkGroupMember { ChunkTemplateId = new ChunkTemplateId(902), CellX = 0, CellZ = 0 },
                            new ChunkGroupMember { ChunkTemplateId = new ChunkTemplateId(903), CellX = 1, CellZ = 0 },
                        ],
                    },
                ],
            });
            await write.SaveChangesAsync();
        }

        IReadOnlyList<ChunkPool> pools = await new ChunkPoolRepository(database).FindAllWithMembershipsAsync();

        ChunkPool pool = Assert.Single(pools, p => p.Name == "test_pool");
        Assert.Equal(901, Assert.Single(pool.Memberships).ChunkTemplateId.Value);
        Assert.Equal([902, 903], Assert.Single(pool.Groups).Members.Select(m => m.ChunkTemplateId.Value).Order());
    }
}
