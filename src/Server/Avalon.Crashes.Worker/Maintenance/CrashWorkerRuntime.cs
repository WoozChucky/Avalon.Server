using System.Data.Common;
using System.Diagnostics;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Worker.Diagnostics;
using Avalon.Crashes.Worker.Discovery;
using Avalon.Crashes.Worker.Processing;
using Avalon.Crashes.Worker.Storage;
using Avalon.Crashes.Worker.Symbols;
using Avalon.Database.Crashes;

namespace Avalon.Crashes.Worker.Maintenance;

public sealed class CrashWorkerRuntime(CrashMigrator migrator, CrashRetentionService retention,
    CrashMaintenanceStore maintenance, CrashWorkspace workspace, CrashToolRunner tools,
    CrashDiscovery discovery, ICrashQueue queue, CrashProcessor processor, CrashProcessingBackoff backoff,
    CrashWorkerState state, CrashMetrics metrics, CrashToolOptions toolOptions, CrashSymbolOptions symbolOptions,
    TimeProvider time, ILoggerFactory loggers) : ICrashWorkerRuntime
{
    private static readonly ActivitySource s_activities = new(CrashMetrics.MeterName);
    public Task MigrateAsync(CancellationToken ct) => migrator.MigrateAsync(ct);

    public async Task ValidateAsync(CancellationToken ct)
    {
        CrashProcessingBackoffState? observed = backoff.State;
        await workspace.CleanupStaleAsync(ct);
        if (await tools.ValidateToolsAsync(ct) != ToolFailureKind.None)
            throw new InvalidOperationException("Native tools unavailable.");
        backoff.Recovered(observed, CrashBackoffDependency.Tools);
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        while (true)
        {
            await retention.SweepAsync(ct);
            DateTimeOffset now = time.GetUtcNow();
            CrashQueueSnapshot snapshot = await maintenance.SnapshotAsync(now, ct);
            metrics.Snapshot(new(snapshot.Pending, snapshot.OldestReceivedAt is { } oldest ? Math.Max(0, (now - oldest).TotalSeconds) : 0,
                snapshot.LastInventorySweep is { } sweep ? Math.Max(0, (now - sweep).TotalSeconds) : -1,
                now.ToUnixTimeSeconds(), snapshot.Expired, now.AddDays(-90).ToUnixTimeSeconds(),
                DiskBytes(toolOptions.ScratchRoot), DiskBytes(symbolOptions.CacheRoot)));
            if (snapshot.Expired == 0) return;
        }
    }

    public async Task DiscoverAsync(CancellationToken ct)
    {
        CrashProcessingBackoffState? observed = backoff.State;
        if (observed is { } unavailable && unavailable.RetryAt > time.GetUtcNow()) return;
        try
        {
            await discovery.RunCycleAsync(time.GetUtcNow(), ct);
            state.StorageReady = true;
            backoff.Recovered(observed, CrashBackoffDependency.Discovery);
        }
        catch (CrashStorageException ex)
        {
            state.StorageReady = false;
            if (ex.Code == CrashFailureCode.AccessDenied) backoff.Degrade(ex.Code, time.GetUtcNow(), CrashBackoffDependency.Discovery);
            loggers.CreateLogger("Avalon.Crashes.Worker.Events").LogWarning("Worker dependency {reason}", ex.Code);
        }
    }

    public async Task ProcessOneAsync(CancellationToken ct)
    {
        if (backoff.State is { } unavailable && unavailable.RetryAt > time.GetUtcNow()) return;
        CrashLease? lease = await queue.ClaimAsync(time.GetUtcNow(), ct);
        if (lease is null) return;
        long started = time.GetTimestamp();
        using Activity? activity = s_activities.StartActivity("process");
        try { await processor.ProcessAsync(lease, ct); }
        finally { metrics.Duration(time.GetElapsedTime(started)); }
    }

    private static long DiskBytes(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
            return Directory.EnumerateFiles(root, "*", options).Sum(path => new FileInfo(path).Length);
        }
        catch (IOException) { return -1; }
        catch (UnauthorizedAccessException) { return -1; }
    }
}

/// <summary>A database failure fences further intake even when the processor reconciles internally.</summary>
public sealed class CrashGuardedQueue(CrashQueue inner, CrashWorkerState state) : ICrashQueue
{
    public Task<bool> DiscoverAsync(StoredCrashObject source, DateTimeOffset now, CancellationToken ct) => state.DatabaseReady
        ? GuardAsync(() => inner.DiscoverAsync(source, now, ct)) : Task.FromResult(false);
    public async Task<CrashLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!state.IntakeReady) return null;
        CrashLease? lease = await GuardAsync(() => inner.ClaimAsync(now, ct));
        return state.IntakeReady ? lease : null;
    }
    public Task<bool> RenewAsync(CrashLease lease, DateTimeOffset now, CancellationToken ct) => state.DatabaseReady
        ? GuardAsync(() => inner.RenewAsync(lease, now, ct)) : Task.FromResult(false);
    public Task<bool> CompleteAsync(CrashLease lease, CrashAttemptResult result, DateTimeOffset now, CancellationToken ct) => state.DatabaseReady
        ? GuardAsync(() => inner.CompleteAsync(lease, result, now, ct)) : Task.FromResult(false);
    private async Task<T> GuardAsync<T>(Func<Task<T>> work)
    {
        try { return await work(); }
        catch (Exception ex) when (ex is DbException or TimeoutException or IOException)
        {
            state.DatabaseFailed();
            throw;
        }
    }
}

public sealed class CrashGuardedSymbolLock(CrashSymbolLock inner, CrashWorkerState state) : ICrashSymbolLock
{
    public async Task<IAsyncDisposable> AcquireAsync(string cacheVersion, string moduleIdentity, CancellationToken ct)
    {
        try { return new Lease(await inner.AcquireAsync(cacheVersion, moduleIdentity, ct), state); }
        catch (Exception ex) when (ex is DbException or TimeoutException or IOException)
        {
            state.DatabaseFailed();
            throw;
        }
    }
    private sealed class Lease(IAsyncDisposable inner, CrashWorkerState state) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await inner.DisposeAsync(); }
            catch (Exception ex) when (ex is DbException or TimeoutException or IOException)
            {
                state.DatabaseFailed();
                throw;
            }
        }
    }
}
