using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IQuestRepository
{
    /// <summary>Every quest with its stages, objectives (and their drops) and item rewards, untracked. Read whole by the Quests reload area.</summary>
    Task<IReadOnlyCollection<QuestTemplate>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>One page of quests by id, each whole as <see cref="GetAllAsync"/> reads it, untracked (#714).</summary>
    Task<PagedResult<QuestTemplate>> PaginateAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>One quest, whole as <see cref="GetAllAsync"/> reads it, untracked; null when there is none (#714).</summary>
    Task<QuestTemplate?> FindByIdAsync(QuestTemplateId id, CancellationToken cancellationToken = default);
}

public class QuestRepository(IDbContextFactory<WorldDbContext> contextFactory) : IQuestRepository
{
    public async Task<IReadOnlyCollection<QuestTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Whole(context).ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<QuestTemplate>> PaginateAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        int total = await context.QuestTemplates.CountAsync(cancellationToken);
        List<QuestTemplate> items = await Whole(context)
            .OrderBy(q => q.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<QuestTemplate>(page, pageSize, total, items);
    }

    public async Task<QuestTemplate?> FindByIdAsync(QuestTemplateId id, CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Whole(context).FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
    }

    private static IQueryable<QuestTemplate> Whole(WorldDbContext context) =>
        context.QuestTemplates
            .AsNoTracking()
            .AsSplitQuery()
            .Include(q => q.Stages)
            .Include(q => q.Objectives).ThenInclude(o => o.Drops)
            .Include(q => q.ItemRewards);
}
