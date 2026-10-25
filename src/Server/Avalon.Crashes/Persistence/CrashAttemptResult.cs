using Avalon.Crashes.Analysis;

namespace Avalon.Crashes.Persistence;

public sealed record CrashAttemptResult
{
    private CrashAttemptResult(CrashReportStatus status, CrashFailureCode? reason, DateTimeOffset? nextAttemptAt, CrashAnalysis? analysis, SignatureIdentity? signature)
    {
        Status = status;
        Reason = reason;
        NextAttemptAt = nextAttemptAt;
        Analysis = analysis;
        Signature = signature;
    }

    public CrashReportStatus Status { get; }
    public CrashFailureCode? Reason { get; }
    public DateTimeOffset? NextAttemptAt { get; }
    public CrashAnalysis? Analysis { get; }
    public SignatureIdentity? Signature { get; }
    /// <summary>Ephemeral acceptance deadline for a new processed commit; an already accepted token still reconciles.</summary>
    public DateTimeOffset? ProcessedDeadline { get; private init; }

    public static CrashAttemptResult Processed(CrashAnalysis analysis, SignatureIdentity signature, DateTimeOffset? deadline = null)
        => new(CrashReportStatus.Processed, null, null, analysis, signature) { ProcessedDeadline = deadline };
    public static CrashAttemptResult Retry(CrashFailureCode reason, DateTimeOffset nextAttemptAt) => new(CrashReportStatus.RetryWait, reason, nextAttemptAt, null, null);
    public static CrashAttemptResult Rejected(CrashFailureCode reason) => new(CrashReportStatus.Rejected, reason, null, null, null);
    public static CrashAttemptResult Failed(CrashFailureCode reason) => new(CrashReportStatus.Failed, reason, null, null, null);
}
