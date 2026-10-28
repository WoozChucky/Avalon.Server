namespace Avalon.Crashes.Queries;

public sealed record CrashDailyCount(DateOnly Date, long Count);

// Counts on this summary explicitly distinguish lifetime, retained and filter-matching reports.
// No report contents, canonical signature or note text belongs in a summary.
public sealed record CrashGroupSummary(
    CrashGroupId Id, string Signature, int SignatureVersion, CrashGroupStatus Status,
    string? FixedInVersion, string? IssueUrl, bool IsRegression, long RegressionCount,
    DateTimeOffset? LastRegressionAt, long TriageRevision, DateTimeOffset? TriagedAt, DateTimeOffset? FixedAt,
    DateTimeOffset FirstSeen, DateTimeOffset LastSeen, string FirstVersion, string LastVersion,
    long ReportCount, long RetainedReportCount, long MatchingReportCount, IReadOnlyList<CrashDailyCount> DailyCounts);

public sealed record CrashGroupPage(
    IReadOnlyList<CrashGroupSummary> Items, int Page, int PageSize, long Total,
    DateTimeOffset AsOf, int RetentionDays, DateTimeOffset EffectiveSince);

public sealed record CrashVersionCount(string Version, long Count);
public sealed record CrashGpuDriverCount(string Module, string Version, string Source, long Count);
// The client's GPU and its Vulkan driver (schema 2, Client#459); "unknown" for reports without them.
public sealed record CrashGpuCount(string Name, string DriverVersion, long Count);
public sealed record CrashOsBuildCount(string Build, long Count);

public sealed record CrashReportSummary(
    CrashReportId Id, DateTimeOffset ReceivedAt, DateTimeOffset ExpiresAt, string? Version,
    uint? ExceptionCode, CrashQualityFlags Quality, string? CrashingModule);

public sealed record CrashReportPage(IReadOnlyList<CrashReportSummary> Items, int Page, int PageSize, long Total);

public sealed record CrashGroupDetail(
    CrashGroupSummary Group, string? Notes, DateTimeOffset AsOf, DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd, long RetainedReportCount, CrashReportId? LatestReportId,
    DateTimeOffset? LatestStackReceivedAt, CrashThread? LatestStack, CrashQualityFlags? LatestStackQuality,
    IReadOnlyList<CrashVersionCount> ByVersion, IReadOnlyList<CrashGpuDriverCount> ByGpuDriver,
    IReadOnlyList<CrashOsBuildCount> ByOsBuild, CrashReportPage Reports)
{
    public IReadOnlyList<CrashGpuCount> ByGpu { get; init; } = [];
}

// Analysis is the bounded, typed schema, never raw upstream JSON. Non-processed rows have only the safe envelope.
public sealed record CrashReportDetail(
    CrashReportId Id, CrashReportStatus Status, CrashFailureCode? Reason,
    DateTimeOffset ReceivedAt, DateTimeOffset ExpiresAt, DateTimeOffset? ProcessedAt,
    CrashGroupId? GroupId, string? Version, string? GitHash, DateTimeOffset? ClientCrashedAt,
    uint? ExceptionCode, string? ExceptionAddress, string? CrashingModule,
    string? OsVersion, string? OsBuild, string? Cpu, string? GpuDriverModule,
    string? GpuDriverVersion, string? GpuDriverSource, CrashQualityFlags Quality,
    CrashAnalysis? Analysis, string? LogTail);
