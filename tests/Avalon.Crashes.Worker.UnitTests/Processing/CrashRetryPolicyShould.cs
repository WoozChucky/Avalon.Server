using Avalon.Crashes.Persistence;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Processing;

public sealed class CrashRetryPolicyShould
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Should_retry_missing_runtime_symbols_without_creating_a_group()
    {
        CrashAttemptResult result = new Worker.Processing.CrashRetryPolicy().Decide(CrashFailureCode.MissingRuntimeSymbols, Lease(), s_now);
        Assert.Equal(CrashReportStatus.RetryWait, result.Status);
        Assert.Null(result.Analysis);
        Assert.Null(result.Signature);
        Assert.InRange(result.NextAttemptAt!.Value, s_now.AddMinutes(1), s_now.AddSeconds(72));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    [InlineData(99, 60)]
    public void Should_keep_jitter_at_or_above_the_backoff(long attempts, int minutes)
    {
        for (int i = 0; i < 100; i++)
        {
            CrashAttemptResult result = new Worker.Processing.CrashRetryPolicy().Decide(CrashFailureCode.StorageUnavailable, Lease() with { AttemptCount = attempts, RetryCampaignAttemptCount = attempts }, s_now);
            Assert.InRange(result.NextAttemptAt!.Value, s_now.AddMinutes(minutes), s_now.AddMinutes(minutes * 1.2));
        }
    }

    [Fact]
    public void Should_fail_after_three_interrupted_attempts()
    {
        CrashAttemptResult result = new Worker.Processing.CrashRetryPolicy().Decide(CrashFailureCode.ResourceLimit, Lease() with { ConsecutiveToolFailures = 2 }, s_now);
        Assert.Equal(CrashReportStatus.Failed, result.Status);
    }

    [Theory]
    [InlineData(CrashFailureCode.InvalidAnalysis, CrashReportStatus.Failed)]
    [InlineData(CrashFailureCode.UngroupableAnalysis, CrashReportStatus.Failed)]
    [InlineData(CrashFailureCode.RuntimeIdentityMismatch, CrashReportStatus.Rejected)]
    [InlineData(CrashFailureCode.AccessDenied, CrashReportStatus.RetryWait)]
    [InlineData(CrashFailureCode.ConfigurationInvalid, CrashReportStatus.RetryWait)]
    [InlineData(CrashFailureCode.UnsafeArchive, CrashReportStatus.Rejected)]
    public void Should_distinguish_operational_failures_from_malformed_input(CrashFailureCode code, CrashReportStatus status)
        => Assert.Equal(status, new Worker.Processing.CrashRetryPolicy().Decide(code, Lease(), s_now).Status);

    [Fact]
    public void Should_restart_backoff_in_a_new_campaign_without_resetting_lifetime_attempts()
    {
        CrashAttemptResult result = new Worker.Processing.CrashRetryPolicy().Decide(CrashFailureCode.MissingRuntimeSymbols,
            Lease() with { AttemptCount = 100, RetryCampaignAttemptCount = 1 }, s_now);
        Assert.InRange(result.NextAttemptAt!.Value, s_now.AddMinutes(1), s_now.AddSeconds(72));
    }

    [Fact]
    public void Should_stop_at_the_campaign_horizon_and_never_schedule_after_expiry()
    {
        var policy = new Worker.Processing.CrashRetryPolicy();
        Assert.Equal(CrashReportStatus.Failed, policy.Decide(CrashFailureCode.StorageUnavailable,
            Lease() with { RetryCampaignStartedAt = s_now.AddHours(-24) }, s_now).Status);
        CrashLease lease = Lease() with { ExpiresAt = s_now.AddSeconds(30) };
        CrashAttemptResult result = policy.Decide(CrashFailureCode.MissingRuntimeSymbols, lease, s_now);
        Assert.Equal(CrashReportStatus.Failed, result.Status);
        Assert.Null(result.NextAttemptAt);
        Assert.Equal(CrashReportStatus.RetryWait, policy.Decide(CrashFailureCode.MissingRuntimeSymbols,
            Lease() with { FirstAttemptAt = s_now.AddDays(-3) }, s_now).Status);
    }

    internal static CrashLease Lease() => new(Guid.NewGuid(), Guid.NewGuid(),
        new("1.0.0/2026-10-24/11111111111111111111111111111111.zip", "etag", 10, s_now),
        1, 0, s_now, s_now, s_now.AddDays(90), s_now.AddMinutes(10));
}
