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
        // var, not an explicit type: an EF Core query captures this array, and an explicit non-nullable
        // array type makes the compiler add a Convert node to the expression tree EF Core translates.
#pragma warning disable IDE0008
        var idSet = ids.Distinct().ToArray();
#pragma warning restore IDE0008
        if (idSet.Length == 0) return Array.Empty<ItemTemplate>();

        await using WorldDbContext context = await CreateContextAsync(cancellationToken);

        return await context.ItemTemplates
            .AsNoTracking()
            .Where(t => idSet.Contains(t.Id))
            .ToListAsync(cancellationToken);
    }
}
