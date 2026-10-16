using Avalon.Domain.Auth;

namespace Avalon.Infrastructure;

public interface IWorldReadiness
{
    Task<bool> IsReadyAsync(ushort worldId, CancellationToken ct);
}

public sealed class WorldReadiness(IReplicatedCache cache) : IWorldReadiness
{
    public async Task<bool> IsReadyAsync(ushort worldId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return await cache.GetAsync(CacheKeys.WorldReady(worldId)) == "1";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public static WorldStatus Resolve(WorldMaintenanceState state, bool ready, DateTime nowUtc)
        => state.IsCutoffActive(nowUtc) ? WorldStatus.Maintenance
            : ready ? WorldStatus.Online : WorldStatus.Offline;

    /// <summary>
    /// <see cref="Resolve"/>'s status with capacity on top (#967): an <see cref="WorldStatus.Online"/> world whose places
    /// taken have reached its limit is <see cref="WorldStatus.Full"/>. Maintenance and Offline come first.
    /// </summary>
    public static WorldStatus WithCapacity(WorldStatus status, int? limit, int placesTaken) =>
        status == WorldStatus.Online && limit is { } value && placesTaken >= value ? WorldStatus.Full : status;
}
