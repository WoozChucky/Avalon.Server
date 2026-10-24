using Avalon.Crashes.Worker.Processing;
using Avalon.Crashes.Worker.Storage;
using Avalon.Crashes.Worker.Symbols;

namespace Avalon.Crashes.Worker.Diagnostics;

public sealed class CrashHealthyObjectStore(ICrashObjectStore inner, CrashWorkerState state, CrashProcessingBackoff? backoff = null) : ICrashObjectStore
{
    public Task<CrashObjectPage> ListAsync(string prefix, string? delimiter, string? continuationToken, CancellationToken ct) =>
        ObserveAsync(() => inner.ListAsync(prefix, delimiter, continuationToken, ct), CrashBackoffDependency.Discovery);
    public Task<StoredCrashObject?> HeadAsync(string key, CancellationToken ct) => ObserveAsync(() => inner.HeadAsync(key, ct));
    public async Task DownloadAsync(StoredCrashObject expected, string destination, long maxBytes, CancellationToken ct) =>
        await ObserveAsync(async () => { await inner.DownloadAsync(expected, destination, maxBytes, ct); return true; });

    private async Task<T> ObserveAsync<T>(Func<Task<T>> operation, CrashBackoffDependency dependency = CrashBackoffDependency.CrashRead)
    {
        CrashProcessingBackoffState? observed = backoff?.State;
        try
        {
            T result = await operation();
            state.StorageReady = true;
            backoff?.Recovered(observed, dependency);
            return result;
        }
        catch (CrashStorageException ex) when (ex.Code is CrashFailureCode.StorageUnavailable or CrashFailureCode.AccessDenied)
        {
            state.StorageReady = false;
            throw;
        }
    }
}

public sealed class CrashHealthySymbolStore(ICrashSymbolStore inner, CrashWorkerState state, CrashProcessingBackoff? backoff = null) : ICrashSymbolStore
{
    public async Task DownloadOriginalAsync(ModuleIdentity identity, bool pdb, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct)
    {
        CrashProcessingBackoffState? observed = backoff?.State;
        try
        {
            await inner.DownloadOriginalAsync(identity, pdb, destination, maxBytes, budget, ct);
            state.SymbolsSucceeded();
            backoff?.Recovered(observed, CrashBackoffDependency.RequiredSymbols);
        }
        catch (CrashSymbolException ex) when (ex.Code is CrashFailureCode.StorageUnavailable or CrashFailureCode.AccessDenied)
        {
            state.SymbolsFailed();
            throw;
        }
    }

    // Converted symbols are optional accelerators. Cache outages never make valid local analysis fail readiness.
    public Task<SymbolCacheMetadata?> DownloadConvertedAsync(ModuleIdentity identity, string cacheVersion, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct) =>
        inner.DownloadConvertedAsync(identity, cacheVersion, destination, maxBytes, budget, ct);
    public Task UploadConvertedAsync(ModuleIdentity identity, SymbolCacheMetadata metadata, string source, CancellationToken ct) =>
        inner.UploadConvertedAsync(identity, metadata, source, ct);
}
