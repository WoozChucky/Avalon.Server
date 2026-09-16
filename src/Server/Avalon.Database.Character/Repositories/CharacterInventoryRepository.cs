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

public class CharacterInventoryRepository(IDbContextFactory<CharacterDbContext> contextFactory) : ICharacterInventoryRepository
{
    public async Task<CharacterInventory> CreateAsync(CharacterInventory inventory, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.CharacterInventory.AddAsync(inventory, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<IList<CharacterInventory>> CreateAsync(IList<CharacterInventory> inventories, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entityList = new List<CharacterInventory>();
        foreach (var inventory in inventories)
        {
            var entity = await context.CharacterInventory.AddAsync(inventory, cancellationToken);
            entityList.Add(entity.Entity);
        }
        await context.SaveChangesAsync(cancellationToken);
        return entityList;
    }

    public async Task<CharacterInventory> UpdateAsync(CharacterInventory inventory, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = context.CharacterInventory.Update(inventory);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<IReadOnlyCollection<CharacterInventory>> GetByCharacterIdAsync(CharacterId characterId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CharacterInventory
            .AsNoTracking()
            .Where(entity => entity.CharacterId == characterId)
            .ToListAsync(cancellationToken);
    }
}
