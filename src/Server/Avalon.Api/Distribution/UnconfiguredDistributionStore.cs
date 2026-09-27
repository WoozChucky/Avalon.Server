namespace Avalon.Api.Distribution;

/// <summary>
/// The store when <c>Application:Distribution</c> is not set: every call is "downloads are not
/// available", which the API answers with 503, so the rest of the API runs without storage.
/// </summary>
public sealed class UnconfiguredDistributionStore : IDistributionStore
{
    private static DistributionUnavailableException Unavailable() => new("Downloads are not available right now.");

    public Task<ChannelPointer?> GetPointerAsync(Channel channel, CancellationToken ct) => throw Unavailable();

    public Task<StoredManifest?> GetManifestAsync(string manifestKey, CancellationToken ct) => throw Unavailable();

    public Task<IReadOnlyList<StoredObject>> ListManifestsAsync(Channel channel, CancellationToken ct) => throw Unavailable();

    public Task<LauncherRelease?> GetLauncherAsync(CancellationToken ct) => throw Unavailable();

    public Uri Presign(string objectKey, TimeSpan ttl) => throw Unavailable();
}
