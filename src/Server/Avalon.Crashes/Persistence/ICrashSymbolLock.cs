namespace Avalon.Crashes.Persistence;

public interface ICrashSymbolLock
{
    Task<IAsyncDisposable> AcquireAsync(string cacheVersion, string moduleIdentity, CancellationToken ct);
}
