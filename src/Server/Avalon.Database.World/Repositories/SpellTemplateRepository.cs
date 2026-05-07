using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ISpellTemplateRepository : IRepository<AbilityTemplate, AbilityId>
{
    Task<IReadOnlyList<AbilityTemplate>> GetByIdsAsync(
        IEnumerable<AbilityId> ids, CancellationToken cancellationToken = default);
}

public class SpellTemplateRepository : EntityFrameworkRepository<AbilityTemplate, AbilityId>, ISpellTemplateRepository
{
    private readonly WorldDbContext _dbContext;

    public SpellTemplateRepository(WorldDbContext dbContext)
        : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AbilityTemplate>> GetByIdsAsync(
        IEnumerable<AbilityId> ids, CancellationToken cancellationToken = default)
    {
        var idSet = ids.Distinct().ToArray();
        if (idSet.Length == 0) return Array.Empty<AbilityTemplate>();

        return await _dbContext.SpellTemplates
            .AsNoTracking()
            .Where(t => idSet.Contains(t.Id))
            .ToListAsync(cancellationToken);
    }
}
