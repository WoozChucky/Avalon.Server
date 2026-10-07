namespace Avalon.Api.Distribution;

/// <summary>
/// The bucket holding game builds and launcher releases (homelab, Garage). Reads return null for
/// an object that does not exist; they throw <see cref="DistributionUnavailableException" /> when
/// no store is configured.
/// </summary>
public interface IDistributionStore
{
    Task<ChannelPointer?> GetPointerAsync(Channel channel, CancellationToken ct);

    /// <summary>The manifest at <paramref name="manifestKey" /> and its <c>.sig</c>.</summary>
    Task<StoredManifest?> GetManifestAsync(string manifestKey, CancellationToken ct);

    /// <summary>The channel's manifests (<c>manifests/&lt;channel&gt;/*.json</c>), any order.</summary>
    Task<IReadOnlyList<StoredObject>> ListManifestsAsync(Channel channel, CancellationToken ct);

    Task<LauncherRelease?> GetLauncherAsync(CancellationToken ct);

    /// <summary>The changelog entries under <paramref name="prefix" /> (<c>changelog/…/*.json</c>), any order.</summary>
    Task<IReadOnlyList<StoredObject>> ListChangelogAsync(string prefix, CancellationToken ct);

    /// <summary>The entry at <paramref name="key" />, or null when it is missing or cannot be read.</summary>
    Task<ChangelogEntryDto?> GetChangelogEntryAsync(string key, CancellationToken ct);

    /// <summary>A GET URL for <paramref name="objectKey" /> on the public host, valid for <paramref name="ttl" />.</summary>
    Uri Presign(string objectKey, TimeSpan ttl);
}
