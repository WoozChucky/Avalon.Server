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
        await using var context = await CreateContextAsync(ct);

        return await context.ChunkPools.AsNoTracking().Include(p => p.Memberships).ToListAsync(ct);
    }
}
