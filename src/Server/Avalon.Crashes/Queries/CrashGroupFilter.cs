namespace Avalon.Crashes.Queries;

public sealed record CrashGroupFilter(
    CrashGroupStatus? Status = null, string? Version = null, DateTimeOffset? Since = null,
    int Page = 1, int PageSize = 25);
