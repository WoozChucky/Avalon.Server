using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Avalon.Database.World.Repositories;

public interface IItemModifierRepository
{
    /// <summary>
    /// The whole modifier catalog (#1179): every modifier with its group, stat lines and weights, and every template's
    /// links, untracked, built into one <see cref="ItemModifierCatalog" /> that logs and leaves out what it cannot trust.
    /// The world's read; the API reads only what a request names.
    /// </summary>
    Task<ItemModifierCatalog> GetCatalogAsync(CancellationToken ct);

    /// <summary>
    /// <paramref name="template" />'s links and the modifiers they name, each with its group and stat lines and no
    /// weights (what a tooltip or a link preview shows), held to the world's rules on that subset: the catalog's own
    /// refusals but the weight one (no weight is read; SeedIntegrityShould holds that no unique or implicit has one), and
    /// the template's links dropped when they break its rarity (ItemModifierCatalog.RarityRefusal).
    /// </summary>
    Task<ItemModifierCatalog> GetLinkedToAsync(ItemTemplate template, CancellationToken ct);

    /// <summary>
    /// The modifiers of these ids, each with its group and stat lines, no weights and no links: what a character's items
    /// carry, read for their kinds and names.
    /// </summary>
    Task<ItemModifierCatalog> GetByIdsAsync(IReadOnlyCollection<int> modifierIds, CancellationToken ct);
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

        return new ItemModifierCatalog(modifiers, links, Logger());
    }

    public async Task<ItemModifierCatalog> GetLinkedToAsync(ItemTemplate template, CancellationToken ct)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(ct);
        ILogger logger = Logger();
        ItemModifierCatalog catalog = await ItemModifierQueries.LinkedToAsync(context, template.Id, logger, ct);

        IReadOnlyList<ItemModifier> linked = catalog.LinkedTo(template.Id);
        if (linked.Count == 0 || ItemModifierCatalog.RarityRefusal(template.Rarity, template.BaseKind, linked) is not { } reason)
            return catalog;

        logger.LogError("Item template {TemplateId} keeps none of its modifier links: {Reason}", template.Id.Value, reason);
        return catalog.WithoutLinksOf(new HashSet<ulong> { template.Id.Value });
    }

    public async Task<ItemModifierCatalog> GetByIdsAsync(IReadOnlyCollection<int> modifierIds, CancellationToken ct)
    {
        if (modifierIds.Count == 0)
            return new ItemModifierCatalog([], [], Logger());

        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(ct);
        List<ItemModifier> modifiers = await ItemModifierQueries.WithGroupAndStats(context)
            .Where(m => modifierIds.Contains(m.Id))
            .ToListAsync(ct);

        return new ItemModifierCatalog(modifiers, [], Logger());
    }

    private ILogger Logger() => loggerFactory.CreateLogger<ItemModifierCatalog>();
}

/// <summary>
/// The narrow catalog reads (#1179) the API's repository and its template edit share: the modifiers a template links or
/// an item carries, never the whole catalog.
/// </summary>
public static class ItemModifierQueries
{
    /// <summary>The modifiers, untracked, each with its group (one object per group) and its stat lines; no weights.</summary>
    public static IQueryable<ItemModifier> WithGroupAndStats(WorldDbContext context) => context.ItemModifiers
        .AsNoTrackingWithIdentityResolution()
        .AsSplitQuery()
        .Include(m => m.Group)
        .Include(m => m.Stats.OrderBy(s => s.Index))
        .OrderBy(m => m.Id);

    /// <summary>
    /// <paramref name="template" />'s links and the modifiers they name, as a catalog holding only them, with the
    /// catalog's own refusals applied (a link to a modifier it leaves out drops every link of the template).
    /// </summary>
    public static async Task<ItemModifierCatalog> LinkedToAsync(WorldDbContext context, ItemTemplateId template,
        ILogger logger, CancellationToken ct)
    {
        List<ItemTemplateModifier> links = await context.ItemTemplateModifiers.AsNoTracking()
            .Where(l => l.TemplateId == template)
            .ToListAsync(ct);
        if (links.Count == 0)
            return new ItemModifierCatalog([], [], logger);

        List<int> ids = [.. links.Select(l => l.ModifierId).Distinct()];
        List<ItemModifier> modifiers = await WithGroupAndStats(context).Where(m => ids.Contains(m.Id)).ToListAsync(ct);
        return new ItemModifierCatalog(modifiers, links, logger);
    }
}
