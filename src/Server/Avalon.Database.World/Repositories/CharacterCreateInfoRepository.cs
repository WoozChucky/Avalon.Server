using Avalon.Domain.World;
using Avalon.World.Public.Enums;
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
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CharacterCreateInfos
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<CharacterCreateInfo?> GetByClassAsync(CharacterClass @class, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CharacterCreateInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Class == @class, cancellationToken);
    }
}
