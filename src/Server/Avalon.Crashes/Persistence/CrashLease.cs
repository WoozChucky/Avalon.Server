namespace Avalon.Crashes.Persistence;

/// <summary>A durable attempt snapshot. Renewals retain the token; later claims always receive a new one.</summary>
/// <param name="ConsecutiveToolFailures">Completed tool failures and recovered interrupted attempts, excluding this in-flight attempt.</param>
/// <param name="RetryCampaignStartedAt">The current 24-hour retry horizon; a manual retry may reset it without changing lifetime counters.</param>
public sealed record CrashLease(
    CrashReportId ReportId,
    Guid OwnerToken,
    StoredCrashObject Source,
    long AttemptCount,
    long ConsecutiveToolFailures,
    DateTimeOffset FirstAttemptAt,
    DateTimeOffset RetryCampaignStartedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset LeaseUntil);
