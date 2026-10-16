using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IWorldRepository : IRepository<Domain.Auth.World, WorldId>
{
    Task UpdateMetadataAsync(Domain.Auth.World world, CancellationToken ct);

    /// <summary>
    /// Sets the world's player limit (#967), by column: <c>PlayerLimit</c> and <c>UpdatedAt</c> only, so a maintenance
    /// transition or a metadata edit written in between survives. Null when the world does not exist.
    /// </summary>
    Task<PlayerLimitChange?> SetPlayerLimitAsync(WorldId id, int? limit, DateTime nowUtc, CancellationToken ct);
}

/// <summary>A player limit as it was and as it is now (#967), for the change's log line.</summary>
public sealed record PlayerLimitChange(int? Previous, int? Current);

// The factory is both the base repository's and this type's own; both hold the same instance.
#pragma warning disable CS9107
public class WorldRepository(IDbContextFactory<AuthDbContext> contextFactory)
    : EntityFrameworkRepository<Domain.Auth.World, WorldId, AuthDbContext>(contextFactory), IWorldRepository
#pragma warning restore CS9107
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

    public async Task<PlayerLimitChange?> SetPlayerLimitAsync(WorldId id, int? limit, DateTime nowUtc, CancellationToken ct)
    {
        while (true)
        {
            await using AuthDbContext context = await contextFactory.CreateDbContextAsync(ct);
            var current = await context.Worlds.AsNoTracking().Where(w => w.Id == id)
                .Select(w => new { w.PlayerLimit }).SingleOrDefaultAsync(ct);
            if (current is null) return null;

            int changed = await context.Worlds.Where(w => w.Id == id && w.PlayerLimit == current.PlayerLimit)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(w => w.PlayerLimit, limit)
                    .SetProperty(w => w.UpdatedAt, nowUtc), ct);
            if (changed == 1) return new PlayerLimitChange(current.PlayerLimit, limit);
        }
    }
}
