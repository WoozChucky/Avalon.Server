using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IItemInstanceRepository : IRepository<ItemInstance, ItemInstanceId>
{
    Task<IReadOnlyList<ItemInstance>> GetByCharacterIdWithTemplateAsync(
        CharacterId characterId, CancellationToken cancellationToken = default);
}

public class ItemInstanceRepository : EntityFrameworkRepository<ItemInstance, ItemInstanceId>, IItemInstanceRepository
{
    private readonly WorldDbContext _dbContext;

    public ItemInstanceRepository(WorldDbContext dbContext)
        : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ItemInstance>> GetByCharacterIdWithTemplateAsync(
        CharacterId characterId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ItemInstances
            .AsNoTracking()
            .Include(x => x.Template)
            .Where(x => x.CharacterId == characterId)
            .ToListAsync(cancellationToken);
    }
}
