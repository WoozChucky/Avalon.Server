using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ICreatureTemplateRepository : IRepository<CreatureTemplate, CreatureTemplateId>
{

}

public class CreatureTemplateRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : EntityFrameworkRepository<CreatureTemplate, CreatureTemplateId, WorldDbContext>(contextFactory),
        ICreatureTemplateRepository
{
}
