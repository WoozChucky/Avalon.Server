using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IItemInstanceRepository : IRepository<ItemInstance, ItemInstanceId>
{
    Task<IReadOnlyList<ItemInstance>> GetByCharacterIdWithTemplateAsync(
        CharacterId characterId, CancellationToken cancellationToken = default);
}

public class ItemInstanceRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : EntityFrameworkRepository<ItemInstance, ItemInstanceId, WorldDbContext>(contextFactory), IItemInstanceRepository
{
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
