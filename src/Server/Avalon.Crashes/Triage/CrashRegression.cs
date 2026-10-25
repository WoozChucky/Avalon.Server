using Avalon.Crashes.Analysis;

namespace Avalon.Crashes.Triage;

public static class CrashRegression
{
    public static bool ShouldReopen(CrashGroupStatus groupStatus, string? fixedVersion, DateTimeOffset? fixedAt, string reportVersion, DateTimeOffset receivedAt) =>
        groupStatus == CrashGroupStatus.Fixed
        && fixedVersion is not null
        && fixedAt.HasValue
        && receivedAt >= fixedAt.Value
        && CrashVersion.Compare(reportVersion, fixedVersion) is >= 0;
}
