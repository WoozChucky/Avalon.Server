using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface IItemInstanceRepository : IRepository<ItemInstance, ItemInstanceId>
{
    /// <summary>The character's item instances: template id, count, durability, charges, flags, rarity, item level and modifiers with their values.</summary>
    Task<IReadOnlyList<ItemInstance>> GetByCharacterIdAsync(
        CharacterId characterId, CancellationToken cancellationToken = default);
}

public class ItemInstanceRepository(IDbContextFactory<CharacterDbContext> contextFactory)
    : EntityFrameworkRepository<ItemInstance, ItemInstanceId, CharacterDbContext>(contextFactory), IItemInstanceRepository
{
    public async Task<IReadOnlyList<ItemInstance>> GetByCharacterIdAsync(
        CharacterId characterId, CancellationToken cancellationToken = default)
    {
        await using CharacterDbContext context = await CreateContextAsync(cancellationToken);

        // Split: one query per level rather than one wide join repeating each item for every value row.
        return await context.ItemInstances
            .AsNoTracking()
            .Where(x => x.CharacterId == characterId)
            .Include(i => i.Modifiers).ThenInclude(m => m.Values)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }
}
