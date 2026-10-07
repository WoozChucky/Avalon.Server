namespace Avalon.Api.Distribution;

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
