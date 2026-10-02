using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IWorldMaintenanceRepository
{
    Task<WorldMaintenanceState?> ReadAsync(WorldId id, CancellationToken ct);
    Task<WorldMaintenanceState?> TransitionAsync(WorldId id, bool enabled, TimeSpan grace, DateTime nowUtc,
        CancellationToken ct);
}

public sealed class WorldMaintenanceRepository(IDbContextFactory<AuthDbContext> contextFactory)
    : IWorldMaintenanceRepository
{
    public async Task<WorldMaintenanceState?> ReadAsync(WorldId id, CancellationToken ct)
    {
        await using AuthDbContext context = await contextFactory.CreateDbContextAsync(ct);
        return await context.Worlds.AsNoTracking()
            .Where(world => world.Id == id)
            .Select(world => new WorldMaintenanceState(world.MaintenanceEnabled,
                world.MaintenanceRevision, world.MaintenanceDeadlineUtc))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<WorldMaintenanceState?> TransitionAsync(WorldId id, bool enabled, TimeSpan grace,
        DateTime nowUtc, CancellationToken ct)
    {
        DateTime? deadline = enabled ? nowUtc.Add(grace) : null;
        while (true)
        {
            await using AuthDbContext context = await contextFactory.CreateDbContextAsync(ct);
            WorldMaintenanceState? current = await context.Worlds.AsNoTracking()
                .Where(world => world.Id == id)
                .Select(world => new WorldMaintenanceState(world.MaintenanceEnabled,
                    world.MaintenanceRevision, world.MaintenanceDeadlineUtc))
                .SingleOrDefaultAsync(ct);

            if (current is null || current.Enabled == enabled)
                return current;

            int changed = await context.Worlds
                .Where(world => world.Id == id && world.MaintenanceRevision == current.Revision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(world => world.MaintenanceEnabled, enabled)
                    .SetProperty(world => world.MaintenanceRevision, current.Revision + 1)
                    .SetProperty(world => world.MaintenanceDeadlineUtc, deadline)
                    .SetProperty(world => world.UpdatedAt, nowUtc), ct);

            if (changed == 1)
                return new WorldMaintenanceState(enabled, current.Revision + 1, deadline);
        }
    }
}
