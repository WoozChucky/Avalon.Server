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

    /// <summary>
    /// The talent points the named quests pay (#1181), summed in the database: a quest the world database lacks pays
    /// nothing, nor does a repeatable one, which the world's quest catalog refuses when it would pay.
    /// </summary>
    Task<int> SumTalentPointsAsync(IReadOnlyCollection<uint> questIds, CancellationToken cancellationToken = default);
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

    public async Task<int> SumTalentPointsAsync(IReadOnlyCollection<uint> questIds,
        CancellationToken cancellationToken = default)
    {
        if (questIds.Count == 0) return 0;

        // var, not an explicit type: see AbilityTemplateRepository.GetByIdsAsync.
#pragma warning disable IDE0008
        var ids = questIds.Distinct().Select(id => new QuestTemplateId(id)).ToArray();
#pragma warning restore IDE0008
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.QuestTemplates.AsNoTracking()
            .Where(q => ids.Contains(q.Id) && !q.IsRepeatable && q.RewardTalentPoints > 0)
            .SumAsync(q => q.RewardTalentPoints, cancellationToken);
    }

    private static IQueryable<QuestTemplate> Whole(WorldDbContext context) =>
        context.QuestTemplates
            .AsNoTracking()
            .AsSplitQuery()
            .Include(q => q.Stages)
            .Include(q => q.Objectives).ThenInclude(o => o.Drops)
            .Include(q => q.ItemRewards)
            .Include(q => q.ItemChoices);
}
