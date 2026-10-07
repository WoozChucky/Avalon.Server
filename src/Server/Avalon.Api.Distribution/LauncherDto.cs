namespace Avalon.Api.Distribution;

public sealed record LauncherDto(string Version, long Size, string Sha256, Uri Url);
