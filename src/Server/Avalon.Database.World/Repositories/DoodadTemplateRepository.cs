using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IDoodadTemplateRepository
{
    /// <summary>Every doodad template with its aura outcomes, untracked. Read whole by the world's doodad catalog.</summary>
    Task<IReadOnlyCollection<DoodadTemplate>> GetAllAsync(CancellationToken cancellationToken = default);
}

public class DoodadTemplateRepository(IDbContextFactory<WorldDbContext> contextFactory) : IDoodadTemplateRepository
{
    public async Task<IReadOnlyCollection<DoodadTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.DoodadTemplates
            .AsNoTracking()
            .Include(d => d.AuraOutcomes.OrderBy(o => o.AuraId))
            .ToListAsync(cancellationToken);
    }
}
