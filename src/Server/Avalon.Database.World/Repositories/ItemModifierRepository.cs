using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Avalon.Database.World.Repositories;

public interface IItemModifierRepository
{
    /// <summary>
    /// The whole modifier catalog (#1179): every modifier with its group, stat lines and weights, and every template's
    /// links, untracked, built into one <see cref="ItemModifierCatalog" /> that logs and leaves out what it cannot trust.
    /// </summary>
    Task<ItemModifierCatalog> GetCatalogAsync(CancellationToken ct);
}

public class ItemModifierRepository(IDbContextFactory<WorldDbContext> contextFactory, ILoggerFactory loggerFactory)
    : IItemModifierRepository
{
    public async Task<ItemModifierCatalog> GetCatalogAsync(CancellationToken ct)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(ct);

        // One group object per group, shared by its tiers.
        List<ItemModifier> modifiers = await context.ItemModifiers
            .AsNoTrackingWithIdentityResolution()
            .AsSplitQuery()
            .Include(m => m.Group)
            .Include(m => m.Stats.OrderBy(s => s.Index))
            .Include(m => m.Weights.OrderBy(w => w.BaseKind))
            .OrderBy(m => m.Id)
            .ToListAsync(ct);
        List<ItemTemplateModifier> links = await context.ItemTemplateModifiers.AsNoTracking().ToListAsync(ct);

        return new ItemModifierCatalog(modifiers, links, loggerFactory.CreateLogger<ItemModifierCatalog>());
    }
}
