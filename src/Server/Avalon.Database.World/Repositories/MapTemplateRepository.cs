using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.Database.World.Repositories;

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
