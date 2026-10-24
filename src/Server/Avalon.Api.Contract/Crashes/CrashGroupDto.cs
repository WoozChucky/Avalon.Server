using System.Text.Json.Serialization;

namespace Avalon.Api.Contract.Crashes;

public sealed record CrashGroupDto(
    Guid Id, string Signature, int SignatureVersion, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FixedInVersion, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? IssueUrl, bool IsRegression, long RegressionCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? LastRegressionAt, long TriageRevision, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? TriagedAt, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? FixedAt,
    DateTimeOffset FirstSeen, DateTimeOffset LastSeen, string FirstVersion, string LastVersion,
    long ReportCount, long RetainedReportCount, long MatchingReportCount, IReadOnlyList<CrashDailyCountDto> DailyCounts);

public sealed record CrashDailyCountDto(DateOnly Date, long Count);

public sealed record CrashGroupTriageDto(
    Guid Id, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Status, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FixedInVersion, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? IssueUrl, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Notes,
    bool IsRegression, long RegressionCount, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? LastRegressionAt,
    long TriageRevision, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? TriagedAt, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? FixedAt);
