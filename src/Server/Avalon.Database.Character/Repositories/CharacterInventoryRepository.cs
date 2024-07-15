using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterInventoryRepository
{
    Task<CharacterInventory> CreateAsync(CharacterInventory inventory);
    Task<CharacterInventory> UpdateAsync(CharacterInventory inventory);
    Task<IReadOnlyCollection<CharacterInventory>> GetByCharacterIdAsync(CharacterId characterId);
}

public class CharacterInventoryRepository : ICharacterInventoryRepository
{
    private readonly CharacterDbContext _dbContext;
    
    public CharacterInventoryRepository(CharacterDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task<CharacterInventory> CreateAsync(CharacterInventory inventory)
    {
        var entity = await _dbContext.CharacterInventory.AddAsync(inventory);
        await _dbContext.SaveChangesAsync();
        return entity.Entity;
    }

    public async Task<CharacterInventory> UpdateAsync(CharacterInventory inventory)
    {
        var entity = _dbContext.CharacterInventory.Update(inventory);
        await _dbContext.SaveChangesAsync();
        return entity.Entity;
    }

    public async Task<IReadOnlyCollection<CharacterInventory>> GetByCharacterIdAsync(CharacterId characterId)
    {
        return await _dbContext.CharacterInventory
            .AsNoTracking()
            .Where(entity => entity.CharacterId == characterId)
            .ToListAsync();
    }
    
}
