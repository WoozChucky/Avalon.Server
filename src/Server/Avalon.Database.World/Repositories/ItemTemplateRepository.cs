using Avalon.Database;
using Avalon.Domain.World;

namespace Avalon.World.Database.Repositories;

public interface IItemTemplateRepository : IRepository<ItemTemplate, ItemTemplateId>
{

}

public class ItemTemplateRepository : EntityFrameworkRepository<ItemTemplate, ItemTemplateId>, IItemTemplateRepository
{
    public ItemTemplateRepository(WorldDbContext dbContext)
        : base(dbContext)
    {
    }
}
