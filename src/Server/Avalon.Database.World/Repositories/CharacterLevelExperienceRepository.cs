using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ICharacterLevelExperienceRepository
{
    Task<IReadOnlyCollection<CharacterLevelExperience>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CharacterLevelExperience?> GetLevelAsync(ushort level, CancellationToken cancellationToken = default);
}

public class CharacterLevelExperienceRepository : ICharacterLevelExperienceRepository
{
    private readonly WorldDbContext _dbContext;

    public CharacterLevelExperienceRepository(WorldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<CharacterLevelExperience>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.CharacterLevelExperiences
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<CharacterLevelExperience?> GetLevelAsync(ushort level, CancellationToken cancellationToken = default)
    {
        return await _dbContext.CharacterLevelExperiences
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Level == level, cancellationToken);
    }
}
