using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterStatsRepository
{
    Task<CharacterStats> CreateAsync(CharacterStats stats, CancellationToken cancellationToken = default);
    Task<CharacterStats> UpdateAsync(CharacterStats stats, CancellationToken cancellationToken = default);
    Task<CharacterStats?> GetByCharacterIdAsync(CharacterId characterId, CancellationToken cancellationToken = default);
}

public class CharacterStatsRepository : ICharacterStatsRepository
{
    private readonly CharacterDbContext _dbContext;

    public CharacterStatsRepository(CharacterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CharacterStats> CreateAsync(CharacterStats stats, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CharacterStats.AddAsync(stats, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<CharacterStats> UpdateAsync(CharacterStats stats, CancellationToken cancellationToken = default)
    {
        var entity = _dbContext.CharacterStats.Update(stats);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<CharacterStats?> GetByCharacterIdAsync(CharacterId characterId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.CharacterStats
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.CharacterId == characterId, cancellationToken);
    }
}
