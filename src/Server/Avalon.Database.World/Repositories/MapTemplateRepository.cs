using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface IMapTemplateRepository : IRepository<MapTemplate, MapTemplateId>
{

}

public class MapTemplateRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : EntityFrameworkRepository<MapTemplate, MapTemplateId, WorldDbContext>(contextFactory), IMapTemplateRepository
{
}
