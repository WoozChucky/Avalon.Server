using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IQuestRepository
{
    /// <summary>Every quest with its stages, objectives (and their drops) and item rewards, untracked. Read whole by the Quests reload area.</summary>
    Task<IReadOnlyCollection<QuestTemplate>> GetAllAsync(CancellationToken cancellationToken = default);
}

public class QuestRepository(IDbContextFactory<WorldDbContext> contextFactory) : IQuestRepository
{
    public async Task<IReadOnlyCollection<QuestTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.QuestTemplates
            .AsNoTracking()
            .AsSplitQuery()
            .Include(q => q.Stages)
            .Include(q => q.Objectives).ThenInclude(o => o.Drops)
            .Include(q => q.ItemRewards)
            .ToListAsync(cancellationToken);
    }
}
