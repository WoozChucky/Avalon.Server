using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ILootTableRepository
{
    /// <summary>Every loot table with its entries, untracked. Read whole by the Loot reload area.</summary>
    Task<IReadOnlyCollection<LootTable>> GetAllAsync(CancellationToken cancellationToken = default);
}

public class LootTableRepository(IDbContextFactory<WorldDbContext> contextFactory) : ILootTableRepository
{
    public async Task<IReadOnlyCollection<LootTable>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.LootTables
            .AsNoTracking()
            .Include(t => t.Entries)
            .ToListAsync(cancellationToken);
    }
}
