namespace Avalon.Crashes.Triage;

public enum CrashTriageOutcome { Updated, Missing, Conflict, Validation }

public enum CrashTriageValidationCode { InvalidRevision, EmptyPatch, InvalidStatus, InvalidFixedVersion, InvalidIssueUrl, NotesTooLong, InvalidNotes }

public sealed record CrashGroupTriage(
    CrashGroupId Id, CrashGroupStatus Status, string? FixedInVersion, string? IssueUrl, string? Notes,
    bool IsRegression, long RegressionCount, DateTimeOffset? LastRegressionAt,
    long TriageRevision, DateTimeOffset? TriagedAt, DateTimeOffset? FixedAt);

public sealed record CrashTriageResult
{
    public CrashTriageOutcome Outcome { get; }
    public CrashGroupTriage? Group { get; }
    public long? CurrentRevision { get; }
    public CrashTriageValidationCode? ValidationCode { get; }

    private CrashTriageResult(CrashTriageOutcome outcome, CrashGroupTriage? group = null, long? currentRevision = null, CrashTriageValidationCode? validationCode = null)
        => (Outcome, Group, CurrentRevision, ValidationCode) = (outcome, group, currentRevision, validationCode);

    public static CrashTriageResult Updated(CrashGroupTriage group) => new(CrashTriageOutcome.Updated, group);
    public static CrashTriageResult Missing() => new(CrashTriageOutcome.Missing);
    public static CrashTriageResult Conflict(long currentRevision) => new(CrashTriageOutcome.Conflict, currentRevision: currentRevision);
    public static CrashTriageResult Invalid(CrashTriageValidationCode code) => new(CrashTriageOutcome.Validation, validationCode: code);
}
