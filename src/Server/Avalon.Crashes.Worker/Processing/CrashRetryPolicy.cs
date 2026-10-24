using Avalon.Crashes.Persistence;

namespace Avalon.Crashes.Worker.Processing;

public sealed class CrashRetryPolicy
{
    public CrashAttemptResult Decide(CrashFailureCode reason, CrashLease lease, DateTimeOffset now)
    {
        if (reason is CrashFailureCode.UnsafeArchive or CrashFailureCode.ArchiveTooLarge
            or CrashFailureCode.MetadataTooLarge or CrashFailureCode.LogTooLarge
            or CrashFailureCode.InvalidMetadata or CrashFailureCode.UnsupportedSchema
            or CrashFailureCode.InvalidDump or CrashFailureCode.UnsupportedArchitecture
            or CrashFailureCode.RuntimeIdentityMismatch or CrashFailureCode.ObjectChanged or CrashFailureCode.InvalidObjectKey)
        {
            return CrashAttemptResult.Rejected(reason);
        }

        bool toolFailure = reason is CrashFailureCode.ToolTimeout or CrashFailureCode.ToolFailed or CrashFailureCode.ResourceLimit;
        if (reason is CrashFailureCode.InvalidAnalysis or CrashFailureCode.UngroupableAnalysis or CrashFailureCode.ObjectMissing
            || toolFailure && lease.ConsecutiveToolFailures >= 2)
        {
            return CrashAttemptResult.Failed(reason);
        }

        DateTimeOffset horizon = lease.RetryCampaignStartedAt.AddHours(24);
        if (horizon > lease.ExpiresAt)
            horizon = lease.ExpiresAt;
        int minutes = lease.RetryCampaignAttemptCount switch { <= 1 => 1, 2 => 5, 3 => 15, _ => 60 };
        var delay = TimeSpan.FromMinutes(minutes * (1 + Random.Shared.NextDouble() * 0.2));
        DateTimeOffset next = now.Add(delay);
        // Never shorten an hourly backoff to squeeze in one more attempt before the cutoff.
        return next >= horizon ? CrashAttemptResult.Failed(reason) : CrashAttemptResult.Retry(reason, next);
    }
}
