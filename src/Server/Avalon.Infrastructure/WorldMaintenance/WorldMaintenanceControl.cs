using System.Globalization;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.Extensions.Logging;

namespace Avalon.Infrastructure.WorldMaintenance;

public interface IWorldMaintenanceControl
{
    Task<WorldMaintenanceState?> SetAsync(WorldId id, bool enabled, TimeSpan grace, string actor,
        CancellationToken ct);
}

public sealed class WorldMaintenanceControl(
    IWorldMaintenanceRepository repository,
    IReplicatedCache cache,
    TimeProvider clock,
    ILogger<WorldMaintenanceControl> logger) : IWorldMaintenanceControl
{
    public async Task<WorldMaintenanceState?> SetAsync(WorldId id, bool enabled, TimeSpan grace,
        string actor, CancellationToken ct)
    {
        WorldMaintenanceState? committed = await repository.TransitionAsync(id, enabled, grace,
            clock.GetUtcNow().UtcDateTime, ct);
        if (committed is null)
            return null;

        logger.LogInformation("World {WorldId} maintenance set to {Enabled} by {Actor}, revision {Revision}",
            id.Value, committed.Enabled, actor, committed.Revision);
        try
        {
            await cache.PublishAsync(CacheKeys.WorldMaintenance(id.Value),
                committed.Revision.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "World {WorldId} maintenance revision {Revision} notification failed",
                id.Value, committed.Revision);
        }

        return committed;
    }
}
