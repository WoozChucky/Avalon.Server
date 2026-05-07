using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ISpellTemplateRepository : IRepository<SpellTemplate, AbilityId>
{
    Task<IReadOnlyList<SpellTemplate>> GetByIdsAsync(
        IEnumerable<AbilityId> ids, CancellationToken cancellationToken = default);
}

public class SpellTemplateRepository : EntityFrameworkRepository<SpellTemplate, AbilityId>, ISpellTemplateRepository
{
    private readonly WorldDbContext _dbContext;

    public SpellTemplateRepository(WorldDbContext dbContext)
        : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<SpellTemplate>> GetByIdsAsync(
        IEnumerable<AbilityId> ids, CancellationToken cancellationToken = default)
    {
        var idSet = ids.Distinct().ToArray();
        if (idSet.Length == 0) return Array.Empty<SpellTemplate>();

        return await _dbContext.SpellTemplates
            .AsNoTracking()
            .Where(t => idSet.Contains(t.Id))
            .ToListAsync(cancellationToken);
    }
}
