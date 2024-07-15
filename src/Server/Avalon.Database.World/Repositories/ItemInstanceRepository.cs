using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Domain.World;

namespace Avalon.World.Database.Repositories;

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
