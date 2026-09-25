using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IItemTemplateRepository : IRepository<ItemTemplate, ItemTemplateId>
{
    /// <summary>The templates with these ids. Duplicates are asked for once; unknown ids are skipped.</summary>
    Task<IReadOnlyList<ItemTemplate>> GetByIdsAsync(
        IEnumerable<ItemTemplateId> ids, CancellationToken cancellationToken = default);
}

public class ItemTemplateRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : EntityFrameworkRepository<ItemTemplate, ItemTemplateId, WorldDbContext>(contextFactory), IItemTemplateRepository
{
    public async Task<IReadOnlyList<ItemTemplate>> GetByIdsAsync(
        IEnumerable<ItemTemplateId> ids, CancellationToken cancellationToken = default)
    {
        var idSet = ids.Distinct().ToArray();
        if (idSet.Length == 0) return Array.Empty<ItemTemplate>();

        await using var context = await CreateContextAsync(cancellationToken);

        return await context.ItemTemplates
            .AsNoTracking()
            .Where(t => idSet.Contains(t.Id))
            .ToListAsync(cancellationToken);
    }
}
