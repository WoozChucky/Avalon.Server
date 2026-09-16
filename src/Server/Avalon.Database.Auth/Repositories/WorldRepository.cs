using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IWorldRepository : IRepository<Domain.Auth.World, WorldId>
{
}

public class WorldRepository(IDbContextFactory<AuthDbContext> contextFactory)
    : EntityFrameworkRepository<Domain.Auth.World, WorldId, AuthDbContext>(contextFactory), IWorldRepository
{
}
