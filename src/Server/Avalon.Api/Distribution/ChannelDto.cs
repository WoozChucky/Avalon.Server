namespace Avalon.Api.Distribution;

public sealed record ChannelDto(string Channel, string Version, string Build, DateTimeOffset PublishedAt, long TotalSize, string Notes);
