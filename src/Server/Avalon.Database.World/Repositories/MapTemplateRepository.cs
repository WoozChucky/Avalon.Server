using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Domain.World;

namespace Avalon.World.Database.Repositories;

public interface IMapTemplateRepository : IRepository<MapTemplate, MapTemplateId>
{

}

public class MapTemplateRepository : EntityFrameworkRepository<MapTemplate, MapTemplateId>, IMapTemplateRepository
{
    public MapTemplateRepository(WorldDbContext dbContext)
        : base(dbContext)
    {
    }
}
