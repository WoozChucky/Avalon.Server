using System.Diagnostics;
using Avalon.Crashes.Analysis;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Worker;
using Avalon.Database.Crashes;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Maintenance;

[Trait("Category", "CrashesPostgres")]
public sealed class CrashRetentionShould : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private static readonly DateTimeOffset s_now = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
    private CrashQueue Queue => new(_postgres.Factory, new Clock());
    public Task InitializeAsync() => _postgres.InitializeAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync();

    [IntegrationFact]
    public async Task Should_run_cleanup_list_and_retry_as_database_only_commands_without_migration()
    {
        CrashLease lease = await ClaimAsync();
        await Queue.CompleteAsync(lease, CrashAttemptResult.Failed(CrashFailureCode.ToolFailed), s_now, default);
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        string connection = db.Database.GetConnectionString()!;
        (int listed, string listing) = await CommandAsync(connection, "list-failures", "--page", "1", "--page-size", "10");
        Assert.Equal(0, listed);
        Assert.Contains(lease.ReportId.Value.ToString(), listing);
        Assert.DoesNotContain("private-etag", listing);
        (int retried, _) = await CommandAsync(connection, "retry", "--report-id", lease.ReportId.Value.ToString());
        Assert.Equal(0, retried);
        (int cleaned, _) = await CommandAsync(connection, "cleanup");
        Assert.Equal(0, cleaned);
        // Removing the schema in this disposable database proves the command does not recreate it.
        await db.Database.ExecuteSqlRawAsync("DROP TABLE crash_reports CASCADE");
        (int missing, string failure) = await CommandAsync(connection, "cleanup");
        Assert.Equal(1, missing);
        Assert.Contains("Crash worker operation failed.", failure);
        Assert.False(await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('crash_reports') IS NOT NULL AS \"Value\"").SingleAsync());
    }

    private static async Task<(int Code, string Output)> CommandAsync(string connection, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(CrashWorkerRegistration).Assembly.Location);
        foreach (string argument in args) start.ArgumentList.Add(argument);
        foreach (string key in start.Environment.Keys.Where(x => x.StartsWith("Crashes__", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["Database__Crashes__ConnectionString"] = connection;
        start.Environment["Crashes__Processing__ToolDirectory"] = "nonexistent-native-tools";
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await output + await error);
    }

    [IntegrationFact]
    public async Task Should_count_only_first_terminal_and_committed_new_group_even_after_manual_retry()
    {
        var observations = new Observations();
        var queue = new CrashQueue(_postgres.Factory, new Clock(), observations);
        var source = new StoredCrashObject($"1.2.3/2026-10-24/{Guid.NewGuid():N}.zip", "private-etag", 1024, s_now);
        await queue.DiscoverAsync(source, s_now, default);
        CrashLease first = (await queue.ClaimAsync(s_now, default))!;
        var failed = CrashAttemptResult.Failed(CrashFailureCode.ToolFailed);
        Assert.True(await queue.CompleteAsync(first, failed, s_now, default));
        Assert.True(await queue.CompleteAsync(first, failed, s_now, default));
        Assert.Equal(1, observations.Terminals);
        var store = new CrashMaintenanceStore(_postgres.Factory, new Clock());
        FailurePage failures = await store.ListFailuresAsync(1, 10, default);
        Assert.Equal(first.ReportId.Value, Assert.Single(failures.Items).ReportId);
        await store.RetryFailedAsync(first.ReportId, s_now, default);
        CrashLease second = (await queue.ClaimAsync(s_now, default))!;
        CrashAnalysis analysis = Analysis();
        var processed = CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis));
        Assert.True(await queue.CompleteAsync(second, processed, s_now, default));
        Assert.True(await queue.CompleteAsync(second, processed, s_now, default));
        Assert.Equal(1, observations.Terminals);
        Assert.Equal(1, observations.Groups);
    }

    [IntegrationFact]
    public async Task Should_recheck_retry_expiry_after_waiting_for_a_row_lock()
    {
        CrashLease lease = await ClaimAsync();
        await Queue.CompleteAsync(lease, CrashAttemptResult.Failed(CrashFailureCode.ToolFailed), s_now, default);
        var clock = new ElapsedClock();
        var store = new CrashMaintenanceStore(_postgres.Factory, clock);
        await using CrashesDbContext blocker = _postgres.Factory.CreateDbContext();
        await using IDbContextTransaction transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Reports.FromSql($"SELECT * FROM crash_reports WHERE id = {lease.ReportId.Value} FOR UPDATE").SingleAsync();
        Task<RetryResult> pending = store.RetryFailedAsync(lease.ReportId, lease.ExpiresAt.AddSeconds(-1), default);
        await using CrashesDbContext observer = _postgres.Factory.CreateDbContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await observer.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database()
            AND wait_event_type = 'Lock' AND query LIKE '%crash_reports%') AS "Value"
            """).SingleAsync(timeout.Token))
        {
            await Task.Delay(10, timeout.Token);
        }
        clock.Advance(TimeSpan.FromSeconds(2));
        await transaction.CommitAsync();
        Assert.Equal(RetryResult.NotEligible, await pending);
        Assert.Equal(CrashReportStatus.Failed, (await observer.Reports.SingleAsync()).Status);
    }

    private sealed class Observations : ICrashQueueObserver
    {
        public int Terminals;
        public int Groups;
        public void Terminal(string? version, CrashReportStatus status) { Terminals++; throw new IOException("export unavailable"); }
        public void GroupCreated() => Groups++;
        public void Retry() { }
        public void LeaseRecovered() { }
    }

    private sealed class ElapsedClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }

    [IntegrationFact]
    public async Task Should_delete_expired_rows_without_decrementing_group_counts()
    {
        CrashLease lease = await ClaimAsync();
        CrashAnalysis analysis = Analysis();
        var result = CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis));
        Assert.True(await _postgres.Queue.CompleteAsync(lease, result, s_now, default));
        var store = new CrashMaintenanceStore(_postgres.Factory, new Clock());
        Assert.Equal(1, await store.DeleteExpiredAsync(s_now.AddDays(91), 500, default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        Assert.Empty(await db.Reports.ToListAsync());
        Assert.Equal(1, (await db.Groups.SingleAsync()).ReportCount);
        Assert.False(await _postgres.Queue.CompleteAsync(lease, result, s_now.AddDays(91), default));
        Assert.Equal(0, await store.DeleteExpiredAsync(s_now.AddDays(91), 500, default));
    }

    [IntegrationFact]
    public async Task Should_reset_only_the_retry_campaign_and_not_retry_expired_or_processed_rows()
    {
        CrashLease lease = await ClaimAsync();
        await _postgres.Queue.CompleteAsync(lease, CrashAttemptResult.Failed(CrashFailureCode.ToolFailed), s_now, default);
        var store = new CrashMaintenanceStore(_postgres.Factory, new Clock());
        Assert.Equal(RetryResult.Retried, await store.RetryFailedAsync(lease.ReportId, s_now.AddHours(2), default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        CrashReport row = await db.Reports.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal(0, row.RetryCampaignAttemptCount);
        Assert.Equal(0, row.ConsecutiveToolFailures);
        Assert.Equal(lease.FirstAttemptAt, row.FirstAttemptAt);
        Assert.Equal(lease.ExpiresAt, row.ExpiresAt);
        Assert.Equal(lease.Source.Key, row.ObjectKey);
        Assert.Equal(s_now.AddHours(2), row.LastManualRetryAt);
        Assert.Equal(s_now.AddHours(2), row.RetryCampaignStartedAt);
        CrashLease next = (await _postgres.Queue.ClaimAsync(s_now.AddHours(2), default))!;
        Assert.Equal(1, next.RetryCampaignAttemptCount);
        CrashAnalysis analysis = Analysis();
        await _postgres.Queue.CompleteAsync(next, CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis)), s_now.AddHours(2), default);
        Assert.Equal(RetryResult.NotEligible, await store.RetryFailedAsync(lease.ReportId, s_now.AddHours(3), default));
        CrashLease expired = await ClaimAsync();
        await _postgres.Queue.CompleteAsync(expired, CrashAttemptResult.Failed(CrashFailureCode.ToolFailed), s_now, default);
        Assert.Equal(RetryResult.NotEligible, await store.RetryFailedAsync(expired.ReportId, s_now.AddDays(91), default));
    }

    private async Task<CrashLease> ClaimAsync()
    {
        await Queue.DiscoverAsync(new($"1.2.3/2026-10-24/{Guid.NewGuid():N}.zip", "private-etag", 1024, s_now), s_now, default);
        return (await Queue.ClaimAsync(s_now, default))!;
    }

    private sealed class Clock : TimeProvider
    {
        public override long GetTimestamp() => 0;
        public override DateTimeOffset GetUtcNow() => s_now;
    }

    private static CrashAnalysis Analysis() => new(1,
        new CrashIdentity("EXCEPTION_ACCESS_VIOLATION", 0xc0000005, "0x0000000000000010", 0, 42),
        new CrashSystem("Windows NT", "10", "1", "x86_64", null),
        [new CrashThread(0, 42, [new CrashFrame("runtime.exe", "0x0000000000000010", "Game::Run", null, null, [], "context") { ImageKey = "IMAGE" }])],
        [new CrashModule("runtime.exe", "IMAGE", "runtime.pdb", "PDB", "1.2.3", "0x0000000000000000", "0x0000000000001000", true)],
        new CrashMetadata(1, "runtime.exe", "1.2.3", "private-hash", 0xc0000005, "0x0000000000000010", 42, s_now),
        "private log", CrashQualityFlags.None, new ProcessorIdentity("0.27.0", "2.3.9", "v1"));
}
