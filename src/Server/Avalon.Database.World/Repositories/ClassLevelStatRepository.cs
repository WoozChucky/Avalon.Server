using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avalon.World.Database.Repositories;

public interface IClassLevelStatRepository
{
    Task<IReadOnlyCollection<ClassLevelStat>> FindAllAsync();
    Task<ClassLevelStat?> GetByLevelAsync(CharacterClass @class, ushort level);
}

public class ClassLevelStatRepository : IClassLevelStatRepository
{
    private readonly WorldDbContext _dbContext;

    public ClassLevelStatRepository(WorldDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task<IReadOnlyCollection<ClassLevelStat>> FindAllAsync()
    {
        return await _dbContext.ClassLevelStats
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<ClassLevelStat?> GetByLevelAsync(CharacterClass @class, ushort level)
    {
        return await _dbContext.ClassLevelStats
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Level == level && entity.Class == @class);
    }
}
