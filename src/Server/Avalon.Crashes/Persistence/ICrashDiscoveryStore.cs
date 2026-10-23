namespace Avalon.Crashes.Persistence;

public interface ICrashDiscoveryStore
{
    /// <summary>Loads at most 100 least recently visited versions and 90 historical dates per version.</summary>
    Task<DiscoveryCursor> LoadAsync(CancellationToken ct);
    /// <summary>Persists opaque cursors and explicit changes after all candidates on each page are durable. Concurrent saves may repeat work.</summary>
    Task SaveAsync(DiscoveryCursor cursor, CancellationToken ct);
}
