using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IAbilityTemplateRepository : IRepository<AbilityTemplate, AbilityId>
{
    Task<IReadOnlyList<AbilityTemplate>> GetByIdsAsync(
        IEnumerable<AbilityId> ids, CancellationToken cancellationToken = default);
}

public class AbilityTemplateRepository : EntityFrameworkRepository<AbilityTemplate, AbilityId>, IAbilityTemplateRepository
{
    private readonly WorldDbContext _dbContext;

    public AbilityTemplateRepository(WorldDbContext dbContext)
        : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AbilityTemplate>> GetByIdsAsync(
        IEnumerable<AbilityId> ids, CancellationToken cancellationToken = default)
    {
        var idSet = ids.Distinct().ToArray();
        if (idSet.Length == 0) return Array.Empty<AbilityTemplate>();

        return await _dbContext.AbilityTemplates
            .AsNoTracking()
            .Where(t => idSet.Contains(t.Id))
            .ToListAsync(cancellationToken);
    }
}
