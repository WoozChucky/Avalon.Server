using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IChunkPoolRepository : IRepository<ChunkPool, ChunkPoolId>
{
    Task<IReadOnlyList<ChunkPool>> FindAllWithMembershipsAsync(CancellationToken ct = default);
}

public class ChunkPoolRepository : EntityFrameworkRepository<ChunkPool, ChunkPoolId>, IChunkPoolRepository
{
    public ChunkPoolRepository(WorldDbContext ctx) : base(ctx) { }

    public async Task<IReadOnlyList<ChunkPool>> FindAllWithMembershipsAsync(CancellationToken ct = default)
        => await DbSet.AsNoTracking().Include(p => p.Memberships).ToListAsync(ct);

    private DbSet<ChunkPool> DbSet => Context.Set<ChunkPool>();
}
