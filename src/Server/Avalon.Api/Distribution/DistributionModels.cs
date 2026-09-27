using System.Text.Json.Serialization;

namespace Avalon.Api.Distribution;

/// <summary>A build channel. Wire names are lower case: "live", "ptr", "dev".</summary>
public enum Channel
{
    Live,
    Ptr,
    Dev,
}

public static class ChannelNames
{
    public static string Wire(this Channel channel) => channel switch
    {
        Channel.Live => "live",
        Channel.Ptr => "ptr",
        Channel.Dev => "dev",
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public static bool TryParse(string? value, out Channel channel)
    {
        switch (value)
        {
            case "live": channel = Channel.Live; return true;
            case "ptr": channel = Channel.Ptr; return true;
            case "dev": channel = Channel.Dev; return true;
            default: channel = default; return false;
        }
    }
}

/// <summary><c>channels/&lt;channel&gt;.json</c>: which build is current. Moving it is the release.</summary>
public sealed record ChannelPointer(string Build, string Manifest);

/// <summary>A manifest as stored: the parsed document, its exact bytes and its Ed25519 signature.</summary>
/// <remarks>
/// The raw JSON is handed back unchanged: the launcher verifies the signature over these bytes, so
/// a re-serialised copy would fail verification.
/// </remarks>
public sealed record StoredManifest(ManifestDocument Document, string Json, string Signature);

public sealed record ManifestDocument(
    int Schema,
    string Channel,
    string Platform,
    string Version,
    string Build,
    DateTimeOffset PublishedAt,
    string MinLauncherVersion,
    string Notes,
    long TotalSize,
    IReadOnlyList<ManifestFile> Files);

public sealed record ManifestFile(string Path, long Size, string Sha256);

/// <summary>A listed object: its key and when it was last written.</summary>
public sealed record StoredObject(string Key, DateTimeOffset Modified);

/// <summary><c>launcher/latest.json</c>: Tauri's updater fields plus the installer (object keys, not URLs).</summary>
public sealed record LauncherRelease(
    string Version,
    string Notes,
    DateTimeOffset PubDate,
    string SignatureWindows,
    string UpdateKey,
    string InstallerKey,
    long InstallerSize,
    string InstallerSha256);

/// <summary>No store is configured, or what it holds is not usable. The API answers 503.</summary>
public sealed class DistributionUnavailableException(string message) : Exception(message);

// ---- Responses ----

public sealed record LauncherDto(string Version, long Size, string Sha256, Uri Url);

/// <summary>Tauri's updater response.</summary>
public sealed record TauriUpdateDto(
    string Version,
    string Notes,
    [property: JsonPropertyName("pub_date")] DateTimeOffset PubDate,
    IReadOnlyDictionary<string, TauriPlatformDto> Platforms);

public sealed record TauriPlatformDto(string Signature, Uri Url);

public sealed record ChannelDto(string Channel, string Version, string Build, DateTimeOffset PublishedAt, long TotalSize, string Notes);

public sealed record ReleaseDto(string Channel, string Version, string Build, DateTimeOffset PublishedAt, string Notes);

/// <summary>The manifest's exact bytes, its signature, and a presigned URL for each distinct blob.</summary>
public sealed record ManifestResponse(string Manifest, string Signature, IReadOnlyDictionary<string, Uri> Urls);
