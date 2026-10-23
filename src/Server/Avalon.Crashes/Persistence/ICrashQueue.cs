namespace Avalon.Crashes.Persistence;

public interface ICrashQueue
{
    Task<bool> DiscoverAsync(StoredCrashObject source, DateTimeOffset now, CancellationToken ct);
    Task<CrashLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct);
    Task<bool> RenewAsync(CrashLease lease, DateTimeOffset now, CancellationToken ct);
    /// <summary>Returns true for an accepted result, including reconciliation of a previously accepted token. A stale or expired lease returns false.</summary>
    Task<bool> CompleteAsync(CrashLease lease, CrashAttemptResult result, DateTimeOffset now, CancellationToken ct);
}
