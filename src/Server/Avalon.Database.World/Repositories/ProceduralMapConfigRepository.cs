using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IProceduralMapConfigRepository
{
    Task<ProceduralMapConfig?> FindByTemplateIdAsync(MapTemplateId id, CancellationToken ct = default);
    Task<IReadOnlyList<ProceduralMapConfig>> FindAllAsync(CancellationToken ct = default);
}

public class ProceduralMapConfigRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : IProceduralMapConfigRepository
{
    public async Task<ProceduralMapConfig?> FindByTemplateIdAsync(MapTemplateId id, CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.ProceduralMapConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.MapTemplateId == id, ct);
    }

    public async Task<IReadOnlyList<ProceduralMapConfig>> FindAllAsync(CancellationToken ct = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        return await context.ProceduralMapConfigs.AsNoTracking().ToListAsync(ct);
    }
}
