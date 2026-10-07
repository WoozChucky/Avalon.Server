using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
        await using CharacterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        EntityEntry<CharacterInventory> entity = context.TrackForInsert(inventory);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<IList<CharacterInventory>> CreateAsync(IList<CharacterInventory> inventories, CancellationToken cancellationToken = default)
    {
        await using CharacterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entityList = new List<CharacterInventory>();
        foreach (CharacterInventory inventory in inventories)
        {
            EntityEntry<CharacterInventory> entity = context.TrackForInsert(inventory);
            entityList.Add(entity.Entity);
        }
        await context.SaveChangesAsync(cancellationToken);
        return entityList;
    }

    public async Task<CharacterInventory> UpdateAsync(CharacterInventory inventory, CancellationToken cancellationToken = default)
    {
        await using CharacterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        EntityEntry<CharacterInventory> entity = context.TrackForUpdate(inventory);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<IReadOnlyCollection<CharacterInventory>> GetByCharacterIdAsync(CharacterId characterId, CancellationToken cancellationToken = default)
    {
        await using CharacterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CharacterInventory
            .AsNoTracking()
            .Where(entity => entity.CharacterId == characterId)
            .ToListAsync(cancellationToken);
    }
}
