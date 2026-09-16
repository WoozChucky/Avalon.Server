using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IChunkTemplateRepository : IRepository<ChunkTemplate, ChunkTemplateId>
{
    Task<IReadOnlyList<ChunkTemplate>> FindAllWithSlotsAsync(CancellationToken ct = default);
}

public class ChunkTemplateRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : EntityFrameworkRepository<ChunkTemplate, ChunkTemplateId, WorldDbContext>(contextFactory), IChunkTemplateRepository
{
    public async Task<IReadOnlyList<ChunkTemplate>> FindAllWithSlotsAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct);

        return await context.ChunkTemplates.AsNoTracking().ToListAsync(ct);
    }
}
