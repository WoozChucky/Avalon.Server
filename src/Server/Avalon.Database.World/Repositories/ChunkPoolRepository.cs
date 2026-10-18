using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IChunkPoolRepository : IRepository<ChunkPool, ChunkPoolId>
{
    Task<IReadOnlyList<ChunkPool>> FindAllWithMembershipsAsync(CancellationToken ct = default);
}

public class ChunkPoolRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : EntityFrameworkRepository<ChunkPool, ChunkPoolId, WorldDbContext>(contextFactory), IChunkPoolRepository
{
    public async Task<IReadOnlyList<ChunkPool>> FindAllWithMembershipsAsync(CancellationToken ct = default)
    {
        await using WorldDbContext context = await CreateContextAsync(ct);

        return await context.ChunkPools.AsNoTracking()
            // In chunk id order: the rows come back in no promised order, and the generator's candidates follow the pool's.
            .Include(p => p.Memberships.OrderBy(m => m.ChunkTemplateId))
            .Include(p => p.Groups).ThenInclude(g => g.Members)
            // Two collections: one query per collection rather than their cartesian product (and EF's warning about it).
            .AsSplitQuery()
            .ToListAsync(ct);
    }
}
