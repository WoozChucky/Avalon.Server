using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Domain.World;

namespace Avalon.World.Database.Repositories;

public interface ISpellTemplateRepository : IRepository<SpellTemplate, SpellId>
{

}

public class SpellTemplateRepository : EntityFrameworkRepository<SpellTemplate, SpellId>, ISpellTemplateRepository
{
    public SpellTemplateRepository(WorldDbContext dbContext)
        : base(dbContext)
    {
    }
}
