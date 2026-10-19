namespace Avalon.Api.Distribution;

public sealed record CrashReportRequest(string Version, string GitHash, long Size);
