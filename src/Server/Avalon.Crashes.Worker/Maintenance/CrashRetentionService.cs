using Avalon.Crashes.Persistence;

namespace Avalon.Crashes.Worker.Maintenance;

public sealed class CrashRetentionService(ICrashMaintenanceStore store, TimeProvider time)
{
    public async Task<long> SweepAsync(CancellationToken ct)
    {
        long removed = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            DateTimeOffset cutoff = time.GetUtcNow();
            int count = await store.DeleteExpiredAsync(cutoff, 500, ct);
            removed += count;
            if (count == 0)
                return removed;
        }
    }
}
