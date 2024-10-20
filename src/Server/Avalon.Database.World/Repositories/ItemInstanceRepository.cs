using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.Database.World.Repositories;

public interface IItemInstanceRepository : IRepository<ItemInstance, ItemInstanceId>
{

}

public class ItemInstanceRepository : EntityFrameworkRepository<ItemInstance, ItemInstanceId>, IItemInstanceRepository
{
    public ItemInstanceRepository(WorldDbContext dbContext)
        : base(dbContext)
    {
    }
}
