using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterStatsRepository
{
    Task<CharacterStats> CreateAsync(CharacterStats stats);
    Task<CharacterStats> UpdateAsync(CharacterStats stats);
    Task<CharacterStats?> GetByCharacterIdAsync(CharacterId characterId);
}

public class CharacterStatsRepository : ICharacterStatsRepository
{
    private readonly CharacterDbContext _dbContext;

    public CharacterStatsRepository(CharacterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CharacterStats> CreateAsync(CharacterStats stats)
    {
        var entity = await _dbContext.CharacterStats.AddAsync(stats);
        await _dbContext.SaveChangesAsync();
        return entity.Entity;
    }

    public async Task<CharacterStats> UpdateAsync(CharacterStats stats)
    {
        var entity = _dbContext.CharacterStats.Update(stats);
        await _dbContext.SaveChangesAsync();
        return entity.Entity;
    }

    public async Task<CharacterStats?> GetByCharacterIdAsync(CharacterId characterId)
    {
        return await _dbContext.CharacterStats
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.CharacterId == characterId);
    }
}
