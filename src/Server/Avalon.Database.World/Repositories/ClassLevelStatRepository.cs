using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IClassLevelStatRepository
{
    Task<IReadOnlyCollection<ClassLevelStat>> FindAllAsync(CancellationToken cancellationToken = default);
    Task<ClassLevelStat?> GetByLevelAsync(CharacterClass @class, ushort level, CancellationToken cancellationToken = default);
}

public class ClassLevelStatRepository : IClassLevelStatRepository
{
    private readonly WorldDbContext _dbContext;

    public ClassLevelStatRepository(WorldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<ClassLevelStat>> FindAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ClassLevelStats
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<ClassLevelStat?> GetByLevelAsync(CharacterClass @class, ushort level, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ClassLevelStats
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Level == level && entity.Class == @class, cancellationToken);
    }
}
