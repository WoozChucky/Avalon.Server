using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IWorldRepository : IRepository<Domain.Auth.World, WorldId>
{
    Task UpdateMetadataAsync(Domain.Auth.World world, CancellationToken ct);
}

public class WorldRepository(IDbContextFactory<AuthDbContext> contextFactory)
    : EntityFrameworkRepository<Domain.Auth.World, WorldId, AuthDbContext>(contextFactory), IWorldRepository
{
    public async Task UpdateMetadataAsync(Domain.Auth.World world, CancellationToken ct)
    {
        await using AuthDbContext context = await contextFactory.CreateDbContextAsync(ct);
        await context.Worlds.Where(w => w.Id == world.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(w => w.Name, world.Name)
            .SetProperty(w => w.Host, world.Host)
            .SetProperty(w => w.Port, world.Port)
            .SetProperty(w => w.MinVersion, world.MinVersion)
            .SetProperty(w => w.Version, world.Version)
            .SetProperty(w => w.Type, world.Type)
            .SetProperty(w => w.AccessLevelRequired, world.AccessLevelRequired)
            .SetProperty(w => w.UpdatedAt, world.UpdatedAt), ct);
    }
}
