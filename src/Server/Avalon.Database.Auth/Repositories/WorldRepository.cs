using Avalon.Database;
using Avalon.Domain.Auth;

namespace Avalon.Auth.Database.Repositories;

public interface IWorldRepository : IRepository<World, WorldId>
{
}

public class WorldRepository : EntityFrameworkRepository<World, WorldId>, IWorldRepository
{
    public WorldRepository(AuthDbContext db)
        : base(db)
    { }
}
