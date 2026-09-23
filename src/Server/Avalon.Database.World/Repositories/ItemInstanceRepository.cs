using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IItemInstanceRepository : IRepository<ItemInstance, ItemInstanceId>
{
    /// <summary>
    /// The character's item instances and nothing else. For callers that need only what the
    /// instance itself carries -- template id, count, durability, flags.
    /// </summary>
    Task<IReadOnlyList<ItemInstance>> GetByCharacterIdAsync(
        CharacterId characterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="GetByCharacterIdAsync" />, with each instance's <see cref="ItemTemplate" />
    /// joined. Only for callers that actually read the template: it is 41 columns per item, so
    /// asking for it when the name and stats go unread is a join for nothing.
    /// </summary>
    Task<IReadOnlyList<ItemInstance>> GetByCharacterIdWithTemplateAsync(
        CharacterId characterId, CancellationToken cancellationToken = default);
}

public class ItemInstanceRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : EntityFrameworkRepository<ItemInstance, ItemInstanceId, WorldDbContext>(contextFactory), IItemInstanceRepository
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

    public async Task<IReadOnlyList<ItemInstance>> GetByCharacterIdWithTemplateAsync(
        CharacterId characterId, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.ItemInstances
            .AsNoTracking()
            .Include(x => x.Template)
            .Where(x => x.CharacterId == characterId)
            .ToListAsync(cancellationToken);
    }
}
