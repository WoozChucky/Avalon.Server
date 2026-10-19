using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Avalon.Api.Distribution;

/// <summary>Anonymous upload grants, limited to small reports from known published releases.</summary>
public sealed class CrashReportService(IDistributionStore store, ICrashReportSlots slots, IMemoryCache cache,
    TimeProvider time, IHostApplicationLifetime lifetime)
{
    public const long MaxSize = 4 * 1024 * 1024;
    private static readonly TimeSpan s_ttl = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _refresh = new(1, 1);

    public async Task<CrashReportSlot?> CreateAsync(CrashReportRequest request, CancellationToken ct)
    {
        if (request.Size is <= 0 or > MaxSize || request.Version is not { Length: > 0 and <= 64 }
            || !request.Version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+')
            || request.GitHash is not { Length: >= 7 and <= 40 } || !request.GitHash.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        HashSet<string> versions = await KnownVersionsAsync(ct);
        if (!versions.Contains(request.Version))
            return null;

        string key = $"{request.Version}/{time.GetUtcNow():yyyy-MM-dd}/{Guid.NewGuid():N}.zip";
        return new CrashReportSlot(slots.PresignCrash(key, request.Size, s_ttl), key);
    }

    private async Task<HashSet<string>> KnownVersionsAsync(CancellationToken ct)
    {
        const string CacheKey = "crash-known-versions";
        await _refresh.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(CacheKey, out HashSet<string>? cached) && cached is not null)
                return cached;

            // A disconnected uploader must not erase a slow history scan's progress. Bound the
            // shared refresh independently, and stop promptly when the host shuts down.
            using var refresh = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
            refresh.CancelAfter(TimeSpan.FromMinutes(2));
            var versions = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            // Historical manifests matter: a player may report a crash after updating the game.
            foreach (Channel channel in Enum.GetValues<Channel>())
            {
                IReadOnlyList<StoredObject> manifests = await store.ListManifestsAsync(channel, refresh.Token);
                await Parallel.ForEachAsync(manifests,
                    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = refresh.Token }, async (manifest, token) =>
                {
                    (string, string, DateTimeOffset) manifestCacheKey = ("crash-manifest-version", manifest.Key, manifest.Modified);
                    if (!cache.TryGetValue(manifestCacheKey, out string? version))
                    {
                        if (await store.GetManifestAsync(manifest.Key, token) is not { } published)
                            return;
                        version = published.Document.Version;
                        cache.Set(manifestCacheKey, version, TimeSpan.FromDays(1));
                    }
                    if (version is not null)
                        versions.TryAdd(version, 0);
                });
            }

            var known = versions.Keys.ToHashSet(StringComparer.Ordinal);
            cache.Set(CacheKey, known, TimeSpan.FromMinutes(1));
            return known;
        }
        finally
        {
            _refresh.Release();
        }
    }
}
