using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ICreatureRarityModifierRepository
{
    Task<IReadOnlyCollection<CreatureRarityModifier>> GetAllAsync(CancellationToken cancellationToken = default);
}

public class CreatureRarityModifierRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : ICreatureRarityModifierRepository
{
    public async Task<IReadOnlyCollection<CreatureRarityModifier>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CreatureRarityModifiers
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
