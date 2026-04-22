using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterInventoryRepository
{
    Task<CharacterInventory> CreateAsync(CharacterInventory inventory, CancellationToken cancellationToken = default);
    Task<IList<CharacterInventory>> CreateAsync(IList<CharacterInventory> inventories, CancellationToken cancellationToken = default);
    Task<CharacterInventory> UpdateAsync(CharacterInventory inventory, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CharacterInventory>> GetByCharacterIdAsync(CharacterId characterId, CancellationToken cancellationToken = default);
}

public class CharacterInventoryRepository : ICharacterInventoryRepository
{
    private readonly CharacterDbContext _dbContext;

    public CharacterInventoryRepository(CharacterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CharacterInventory> CreateAsync(CharacterInventory inventory, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CharacterInventory.AddAsync(inventory, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<IList<CharacterInventory>> CreateAsync(IList<CharacterInventory> inventories, CancellationToken cancellationToken = default)
    {
        var entityList = new List<CharacterInventory>();
        foreach (var inventory in inventories)
        {
            var entity = await _dbContext.CharacterInventory.AddAsync(inventory, cancellationToken);
            entityList.Add(entity.Entity);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entityList;
    }

    public async Task<CharacterInventory> UpdateAsync(CharacterInventory inventory, CancellationToken cancellationToken = default)
    {
        var entity = _dbContext.CharacterInventory.Update(inventory);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<IReadOnlyCollection<CharacterInventory>> GetByCharacterIdAsync(CharacterId characterId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.CharacterInventory
            .AsNoTracking()
            .Where(entity => entity.CharacterId == characterId)
            .ToListAsync(cancellationToken);
    }

}
