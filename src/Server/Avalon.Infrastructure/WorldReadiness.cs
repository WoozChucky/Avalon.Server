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
}
