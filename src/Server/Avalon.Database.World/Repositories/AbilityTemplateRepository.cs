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
        var idSet = ids.Distinct().ToArray();
        if (idSet.Length == 0) return Array.Empty<AbilityTemplate>();

        await using var context = await CreateContextAsync(cancellationToken);

        return await context.AbilityTemplates
            .AsNoTracking()
            .Where(t => idSet.Contains(t.Id))
            .ToListAsync(cancellationToken);
    }
}
