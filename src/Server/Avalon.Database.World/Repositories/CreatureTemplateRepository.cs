using Avalon.Database;
using Avalon.Domain.World;

namespace Avalon.World.Database.Repositories;

public interface ICreatureTemplateRepository : IRepository<CreatureTemplate, CreatureTemplateId>
{

}

public class CreatureTemplateRepository : EntityFrameworkRepository<CreatureTemplate, CreatureTemplateId>, ICreatureTemplateRepository
{
    public CreatureTemplateRepository(WorldDbContext dbContext)
        : base(dbContext)
    {
    }
}
