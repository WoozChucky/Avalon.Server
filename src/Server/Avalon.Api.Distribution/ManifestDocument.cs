namespace Avalon.Api.Distribution;

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
