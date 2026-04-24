using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IChunkTemplateRepository : IRepository<ChunkTemplate, ChunkTemplateId>
{
    Task<IReadOnlyList<ChunkTemplate>> FindAllWithSlotsAsync(CancellationToken ct = default);
}

public class ChunkTemplateRepository : EntityFrameworkRepository<ChunkTemplate, ChunkTemplateId>, IChunkTemplateRepository
{
    public ChunkTemplateRepository(WorldDbContext ctx) : base(ctx) { }

    public async Task<IReadOnlyList<ChunkTemplate>> FindAllWithSlotsAsync(CancellationToken ct = default)
        => await DbSet.AsNoTracking().ToListAsync(ct);

    private DbSet<ChunkTemplate> DbSet => Context.Set<ChunkTemplate>();
}
