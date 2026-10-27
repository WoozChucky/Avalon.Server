using Avalon.Common;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ICharacterCreateInfoRepository
{
    Task<IReadOnlyCollection<CharacterCreateInfo>> FindAllAsync(CancellationToken cancellationToken = default);
    Task<CharacterCreateInfo?> GetByClassAsync(CharacterClass @class, CancellationToken cancellationToken = default);
}

public class CharacterCreateInfoRepository(IDbContextFactory<WorldDbContext> contextFactory) : ICharacterCreateInfoRepository
{
    public async Task<IReadOnlyCollection<CharacterCreateInfo>> FindAllAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CharacterCreateInfos
            .AsNoTracking()
            .Include(entity => entity.StartingItems.OrderBy(item => item.Slot))
            .ToListAsync(cancellationToken);
    }

    public async Task<CharacterCreateInfo?> GetByClassAsync(CharacterClass @class, CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CharacterCreateInfos
            .AsNoTracking()
            .Include(entity => entity.StartingItems.OrderBy(item => item.Slot))
            .FirstOrDefaultAsync(entity => entity.Class == @class, cancellationToken);
    }
}
