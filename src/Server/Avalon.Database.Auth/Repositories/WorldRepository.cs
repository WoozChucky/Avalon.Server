using Avalon.Database;
using Avalon.Domain.Auth;

namespace Avalon.Auth.Database.Repositories;

public interface IWorldRepository : IRepository<Domain.Auth.World, WorldId>
{
}

public class WorldRepository : EntityFrameworkRepository<Domain.Auth.World, WorldId>, IWorldRepository
{
    public WorldRepository(AuthDbContext db)
        : base(db)
    { }
}
