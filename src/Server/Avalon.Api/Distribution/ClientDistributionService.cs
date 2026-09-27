using Avalon.Common.Accounts;
using Microsoft.Extensions.Caching.Memory;

namespace Avalon.Api.Distribution;

/// <summary>
/// What the website and the launcher see of the distribution store: the launcher release, the
/// channels an account may use, recent builds' notes, and a channel's manifest with presigned blob
/// URLs. Pointers and manifests are cached briefly: launchers poll at startup, and a manifest's
/// bytes never change once published.
/// </summary>
public sealed class ClientDistributionService(IDistributionStore store, IMemoryCache cache)
{
    public static readonly TimeSpan InstallerTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan BlobTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    private const string WindowsPlatform = "windows-x86_64";

    public async Task<LauncherDto?> GetLauncherAsync(CancellationToken ct)
    {
        LauncherRelease? release = await LauncherAsync(ct);
        return release is null
            ? null
            : new LauncherDto(release.Version, release.InstallerSize, release.InstallerSha256,
                store.Presign(release.InstallerKey, InstallerTtl));
    }

    public async Task<TauriUpdateDto?> GetLauncherUpdateAsync(CancellationToken ct)
    {
        LauncherRelease? release = await LauncherAsync(ct);
        return release is null
            ? null
            : new TauriUpdateDto(release.Version, release.Notes, release.PubDate,
                new Dictionary<string, TauriPlatformDto>(StringComparer.Ordinal)
                {
                    [WindowsPlatform] = new(release.SignatureWindows, store.Presign(release.UpdateKey, BlobTtl)),
                });
    }

    /// <summary>
    /// The published channels <paramref name="caller" /> may use. A channel that is unpublished, or
    /// whose pointer names a manifest that is not there, is left out rather than failing the list.
    /// </summary>
    public async Task<IReadOnlyList<ChannelDto>> ListChannelsAsync(AccountAccessLevel caller, CancellationToken ct)
    {
        var channels = new List<ChannelDto>();
        foreach (Channel channel in ChannelAccess.Visible(caller))
        {
            // One unreadable channel must not take the others down with it.
            StoredManifest? current;
            try
            {
                current = await CurrentAsync(channel, ct);
            }
            catch (DistributionUnavailableException)
            {
                continue;
            }

            if (current?.Document is { } doc)
                channels.Add(new ChannelDto(channel.Wire(), doc.Version, doc.Build, doc.PublishedAt, doc.TotalSize, doc.Notes));
        }

        return channels;
    }

    /// <summary>Recent builds across the channels <paramref name="caller" /> may list, newest first.</summary>
    public async Task<IReadOnlyList<ReleaseDto>> ListReleasesAsync(AccountAccessLevel? caller, int limit, CancellationToken ct)
    {
        var releases = new List<ReleaseDto>();
        foreach (Channel channel in ChannelAccess.Visible(caller))
        {
            try
            {
                IReadOnlyList<StoredObject> listed =
                    await Cached(("dist-list", channel), () => store.ListManifestsAsync(channel, ct)) ?? [];
                foreach (StoredObject obj in listed.OrderByDescending(o => o.Modified).Take(limit))
                {
                    StoredManifest? manifest = await ManifestAsync(obj.Key, ct);
                    if (manifest?.Document is { } doc)
                        releases.Add(new ReleaseDto(channel.Wire(), doc.Version, doc.Build, doc.PublishedAt, doc.Notes));
                }
            }
            catch (DistributionUnavailableException)
            {
                // A channel that cannot be read is left out of the feed rather than failing it.
            }
        }

        return releases.OrderByDescending(r => r.PublishedAt).Take(limit).ToList();
    }

    /// <summary>
    /// The current manifest of <paramref name="channel" />, or null when the caller may not use it
    /// or nothing is published there (both read as "not found", so a hidden channel is not revealed).
    /// </summary>
    /// <exception cref="DistributionUnavailableException">The pointer names a manifest that is missing.</exception>
    public async Task<ManifestResponse?> GetManifestAsync(Channel channel, AccountAccessLevel caller, CancellationToken ct)
    {
        if (!ChannelAccess.Allows(channel, caller))
            return null;

        ChannelPointer? pointer = await PointerAsync(channel, ct);
        if (pointer is null)
            return null;

        StoredManifest manifest = await ManifestAsync(pointer.Manifest, ct)
            ?? throw new DistributionUnavailableException($"The {channel.Wire()} build is not available right now.");

        var urls = manifest.Document.Files
            .Select(f => f.Sha256)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(sha => sha, sha => store.Presign($"blobs/sha256/{sha}", BlobTtl), StringComparer.Ordinal);
        return new ManifestResponse(manifest.Json, manifest.Signature, urls);
    }

    private async Task<StoredManifest?> CurrentAsync(Channel channel, CancellationToken ct)
    {
        ChannelPointer? pointer = await PointerAsync(channel, ct);
        return pointer is null ? null : await ManifestAsync(pointer.Manifest, ct);
    }

    private Task<ChannelPointer?> PointerAsync(Channel channel, CancellationToken ct) =>
        Cached(("dist-pointer", channel), () => store.GetPointerAsync(channel, ct));

    private Task<StoredManifest?> ManifestAsync(string key, CancellationToken ct) =>
        Cached(("dist-manifest", key), () => store.GetManifestAsync(key, ct));

    private Task<LauncherRelease?> LauncherAsync(CancellationToken ct) =>
        Cached("dist-launcher", () => store.GetLauncherAsync(ct));

    /// <summary>
    /// Keeps what the store returned for a minute, except "not there": a manifest read between its
    /// upload and its signature's, or a channel just published, must show up as soon as it is complete.
    /// </summary>
    private async Task<T?> Cached<T>(object key, Func<Task<T?>> read) where T : class
    {
        if (cache.TryGetValue(key, out T? hit))
            return hit;
        T? value = await read();
        if (value is not null)
            cache.Set(key, value, CacheFor);
        return value;
    }
}
