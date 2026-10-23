namespace Avalon.Crashes.Worker.Storage;

/// <summary>S3 operations have a 30-second deadline across SDK retries and response reads. Earlier caller cancellation propagates.</summary>
public interface ICrashObjectStore
{
    /// <summary>Lists one bounded page; prefix is relative to the configured bucket and tokens are opaque.</summary>
    Task<CrashObjectPage> ListAsync(string prefix, string? delimiter, string? continuationToken, CancellationToken ct);
    Task<StoredCrashObject?> HeadAsync(string key, CancellationToken ct);
    /// <summary>Creates a new destination file with a conditional, capped streaming GET. Failed partial files are removed.</summary>
    Task DownloadAsync(StoredCrashObject expected, string destination, long maxBytes, CancellationToken ct);
}

public sealed record CrashObjectPage(IReadOnlyList<StoredCrashObject> Objects, IReadOnlyList<string> CommonPrefixes, string? NextContinuationToken);

public sealed class CrashStorageException(CrashFailureCode code) : Exception("Crash storage operation failed.")
{
    public CrashFailureCode Code { get; } = code;
}

public sealed class CrashContinuationTokenException() : Exception("Crash listing continuation is no longer valid.");
