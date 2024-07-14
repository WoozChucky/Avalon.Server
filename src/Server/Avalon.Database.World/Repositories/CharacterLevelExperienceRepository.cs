using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.World.Database.Repositories;

public interface ICharacterLevelExperienceRepository
{
    Task<IReadOnlyCollection<CharacterLevelExperience>> GetAllAsync();
    Task<CharacterLevelExperience?> GetLevelAsync(ushort level);
}

public class CharacterLevelExperienceRepository : ICharacterLevelExperienceRepository
{
    private readonly WorldDbContext _dbContext;

    public CharacterLevelExperienceRepository(WorldDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task<IReadOnlyCollection<CharacterLevelExperience>> GetAllAsync()
    {
        return await _dbContext.CharacterLevelExperiences
            .AsNoTracking()
            .ToListAsync();
    }
    
    public async Task<CharacterLevelExperience?> GetLevelAsync(ushort level)
    {
        return await _dbContext.CharacterLevelExperiences
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Level == level);
    }
}
