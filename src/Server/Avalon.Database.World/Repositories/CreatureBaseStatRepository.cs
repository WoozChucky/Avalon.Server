using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ICreatureBaseStatRepository
{
    Task<IReadOnlyCollection<CreatureBaseStat>> GetAllAsync(CancellationToken cancellationToken = default);
}

public class CreatureBaseStatRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : ICreatureBaseStatRepository
{
    public async Task<IReadOnlyCollection<CreatureBaseStat>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CreatureBaseStats
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
