using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ICharacterCreateInfoRepository
{
    Task<IReadOnlyCollection<CharacterCreateInfo>> FindAllAsync();
    Task<CharacterCreateInfo?> GetByClassAsync(CharacterClass @class);
}

public class CharacterCreateInfoRepository : ICharacterCreateInfoRepository
{
    private readonly WorldDbContext _dbContext;

    public CharacterCreateInfoRepository(WorldDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task<IReadOnlyCollection<CharacterCreateInfo>> FindAllAsync()
    {
        return await _dbContext.CharacterCreateInfos
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<CharacterCreateInfo?> GetByClassAsync(CharacterClass @class)
    {
        return await _dbContext.CharacterCreateInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Class == @class);
    }
    
}
