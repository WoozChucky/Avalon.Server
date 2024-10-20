using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.Database.World.Repositories;

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
