using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IVendorStockRepository
{
    /// <summary>Every vendor stock row with its costs, untracked. Read whole by the Vendors reload area.</summary>
    Task<IReadOnlyCollection<VendorStock>> GetAllAsync(CancellationToken cancellationToken = default);
}

public class VendorStockRepository(IDbContextFactory<WorldDbContext> contextFactory) : IVendorStockRepository
{
    public async Task<IReadOnlyCollection<VendorStock>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.VendorStocks
            .AsNoTracking()
            .Include(s => s.Costs)
            .ToListAsync(cancellationToken);
    }
}
