using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IAuraTemplateRepository
{
    /// <summary>Every aura with its modifiers (by stat), untracked. Read whole by the Auras reload area and the Abilities one.</summary>
    Task<IReadOnlyCollection<AuraTemplate>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>One page of auras by id, each with its modifiers, untracked.</summary>
    Task<PagedResult<AuraTemplate>> PaginateAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>One aura with its modifiers, untracked; null when there is none.</summary>
    Task<AuraTemplate?> FindByIdAsync(AuraId id, CancellationToken cancellationToken = default);
}

public class AuraTemplateRepository(IDbContextFactory<WorldDbContext> contextFactory) : IAuraTemplateRepository
{
    public async Task<IReadOnlyCollection<AuraTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Whole(context).ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AuraTemplate>> PaginateAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        int total = await context.AuraTemplates.CountAsync(cancellationToken);
        List<AuraTemplate> items = await Whole(context)
            .OrderBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<AuraTemplate>(page, pageSize, total, items);
    }

    public async Task<AuraTemplate?> FindByIdAsync(AuraId id, CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Whole(context).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    private static IQueryable<AuraTemplate> Whole(WorldDbContext context) =>
        context.AuraTemplates.AsNoTracking().Include(a => a.Modifiers.OrderBy(m => m.Stat));
}
