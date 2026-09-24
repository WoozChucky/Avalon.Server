using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IDialogueRepository
{
    Task<IReadOnlyCollection<DialogueNode>> GetAllNodesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<DialogueOption>> GetAllOptionsAsync(CancellationToken cancellationToken = default);
}

public class DialogueRepository(IDbContextFactory<WorldDbContext> contextFactory) : IDialogueRepository
{
    public async Task<IReadOnlyCollection<DialogueNode>> GetAllNodesAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.DialogueNodes.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<DialogueOption>> GetAllOptionsAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.DialogueOptions.AsNoTracking().ToListAsync(cancellationToken);
    }
}
