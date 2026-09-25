using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface IItemInstanceRepository : IRepository<ItemInstance, ItemInstanceId>
{
    /// <summary>The character's item instances: template id, count, durability, charges and flags.</summary>
    Task<IReadOnlyList<ItemInstance>> GetByCharacterIdAsync(
        CharacterId characterId, CancellationToken cancellationToken = default);
}

public class ItemInstanceRepository(IDbContextFactory<CharacterDbContext> contextFactory)
    : EntityFrameworkRepository<ItemInstance, ItemInstanceId, CharacterDbContext>(contextFactory), IItemInstanceRepository
{
    public async Task<IReadOnlyList<ItemInstance>> GetByCharacterIdAsync(
        CharacterId characterId, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.ItemInstances
            .AsNoTracking()
            .Where(x => x.CharacterId == characterId)
            .ToListAsync(cancellationToken);
    }
}
