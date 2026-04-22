using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ICharacterCreateInfoRepository
{
    Task<IReadOnlyCollection<CharacterCreateInfo>> FindAllAsync(CancellationToken cancellationToken = default);
    Task<CharacterCreateInfo?> GetByClassAsync(CharacterClass @class, CancellationToken cancellationToken = default);
}

public class CharacterCreateInfoRepository : ICharacterCreateInfoRepository
{
    private readonly WorldDbContext _dbContext;

    public CharacterCreateInfoRepository(WorldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<CharacterCreateInfo>> FindAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.CharacterCreateInfos
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<CharacterCreateInfo?> GetByClassAsync(CharacterClass @class, CancellationToken cancellationToken = default)
    {
        return await _dbContext.CharacterCreateInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Class == @class, cancellationToken);
    }

}
