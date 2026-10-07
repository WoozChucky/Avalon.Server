using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IAbilityTemplateRepository : IRepository<AbilityTemplate, AbilityId>
{
    Task<IReadOnlyList<AbilityTemplate>> GetByIdsAsync(
        IEnumerable<AbilityId> ids, CancellationToken cancellationToken = default);
}

public class AbilityTemplateRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : EntityFrameworkRepository<AbilityTemplate, AbilityId, WorldDbContext>(contextFactory), IAbilityTemplateRepository
{
    public async Task<IReadOnlyList<AbilityTemplate>> GetByIdsAsync(
        IEnumerable<AbilityId> ids, CancellationToken cancellationToken = default)
    {
        // var, not an explicit type: an EF Core query captures this array, and an explicit non-nullable
        // array type makes the compiler add a Convert node to the expression tree EF Core translates.
#pragma warning disable IDE0008
        var idSet = ids.Distinct().ToArray();
#pragma warning restore IDE0008
        if (idSet.Length == 0) return Array.Empty<AbilityTemplate>();

        await using WorldDbContext context = await CreateContextAsync(cancellationToken);

        return await context.AbilityTemplates
            .AsNoTracking()
            .Where(t => idSet.Contains(t.Id))
            .ToListAsync(cancellationToken);
    }
}
