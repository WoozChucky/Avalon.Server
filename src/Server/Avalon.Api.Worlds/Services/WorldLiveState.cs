using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.WorldPlaces;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Worlds.Services;

/// <summary>
/// A world's status and occupancy as read right now: readiness and the places ledger, with maintenance and capacity
/// applied (#967). One read for the admin list and the public endpoint, so the two never disagree.
/// </summary>
public readonly record struct WorldLiveState(WorldStatus Status, bool Ready, WorldOccupancy Occupancy)
{
    public static async Task<WorldLiveState> ReadAsync(IWorldReadiness readiness, IWorldOccupancy occupancy, WorldEntity world,
        DateTime nowUtc, CancellationToken ct)
    {
        Task<bool> readyTask = readiness.IsReadyAsync(world.Id.Value, ct);
        Task<WorldOccupancy> occupancyTask = occupancy.ReadAsync(world.Id.Value, ct);
        await Task.WhenAll(readyTask, occupancyTask);

        bool ready = readyTask.Result;
        WorldOccupancy taken = occupancyTask.Result;
        var maintenance = new WorldMaintenanceState(world.MaintenanceEnabled, world.MaintenanceRevision,
            world.MaintenanceDeadlineUtc);
        WorldStatus status = WorldReadiness.WithCapacity(WorldReadiness.Resolve(maintenance, ready, nowUtc),
            world.PlayerLimit, taken.PlayerCount);
        return new WorldLiveState(status, ready, taken);
    }
}
