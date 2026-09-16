using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IItemTemplateRepository : IRepository<ItemTemplate, ItemTemplateId>
{

}

public class ItemTemplateRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : EntityFrameworkRepository<ItemTemplate, ItemTemplateId, WorldDbContext>(contextFactory), IItemTemplateRepository
{
}
