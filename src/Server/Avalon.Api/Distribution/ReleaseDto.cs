namespace Avalon.Api.Distribution;

public sealed record ReleaseDto(string Channel, string Version, string Build, DateTimeOffset PublishedAt, string Notes);
