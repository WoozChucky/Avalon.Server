namespace Avalon.Crashes.Persistence;

public interface ICrashMaintenanceStore
{
    Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct);
    Task<FailurePage> ListFailuresAsync(int page, int pageSize, CancellationToken ct);
    Task<RetryResult> RetryFailedAsync(CrashReportId id, DateTimeOffset now, CancellationToken ct);
}

public sealed record CrashFailure(Guid ReportId, CrashReportStatus Status, CrashFailureCode? Reason,
    DateTimeOffset ReceivedAt, DateTimeOffset ExpiresAt, long AttemptCount);
public sealed record FailurePage(int Page, int PageSize, IReadOnlyList<CrashFailure> Items);
public enum RetryResult { Retried, NotEligible }
