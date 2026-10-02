using Avalon.Domain.Auth;

namespace Avalon.Infrastructure.WorldMaintenance;

public interface IWorldMaintenanceControl
{
    Task<WorldMaintenanceState?> SetAsync(WorldId id, bool enabled, TimeSpan grace, string actor,
        CancellationToken ct);
}
