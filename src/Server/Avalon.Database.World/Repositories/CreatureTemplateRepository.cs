using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.Database.World.Repositories;

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
