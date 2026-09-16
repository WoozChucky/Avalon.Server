using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IClassLevelStatRepository
{
    Task<IReadOnlyCollection<ClassLevelStat>> FindAllAsync(CancellationToken cancellationToken = default);
    Task<ClassLevelStat?> GetByLevelAsync(CharacterClass @class, ushort level, CancellationToken cancellationToken = default);
}

public class ClassLevelStatRepository(IDbContextFactory<WorldDbContext> contextFactory) : IClassLevelStatRepository
{
    public async Task<IReadOnlyCollection<ClassLevelStat>> FindAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ClassLevelStats
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<ClassLevelStat?> GetByLevelAsync(CharacterClass @class, ushort level, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ClassLevelStats
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Level == level && entity.Class == @class, cancellationToken);
    }
}
