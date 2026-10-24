using System.Data.Common;
using Avalon.Crashes.Analysis;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Triage;
using Avalon.Database.Crashes;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Triage;

[Trait("Category", "CrashesPostgres")]
public sealed class CrashTriageStoreShould : IAsyncLifetime
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
    private readonly PostgresFixture _postgres = new();
    private CrashTriageStore Store => new(_postgres.Factory);

    public Task InitializeAsync() => _postgres.InitializeAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync();

    [IntegrationFact]
    public async Task Should_preserve_fixed_at_when_only_notes_change()
    {
        CrashGroup group = await SeedAsync();
        CrashTriageResult result = await Store.UpdateAsync(group.Id, new(5, Notes: new(true, "reviewed")), s_now.AddMinutes(2), default);
        Assert.Equal(CrashTriageOutcome.Updated, result.Outcome);
        Assert.Equal(s_now, result.Group!.FixedAt);
        Assert.Equal(s_now.AddMinutes(2), result.Group.TriagedAt);
        Assert.Equal(6, result.Group.TriageRevision);
        Assert.Equal("reviewed", result.Group.Notes);
        Assert.Equal(1, (await LoadAsync()).ReportCount);

        result = await Store.UpdateAsync(group.Id, new(6, IssueUrl: new(true, "https://github.com/owner/repo/issues/1")), s_now.AddMinutes(3), default);
        Assert.Equal(s_now, result.Group!.FixedAt);
        result = await Store.UpdateAsync(group.Id, new(7, FixedInVersion: new(true, "1.2.3")), s_now.AddMinutes(4), default);
        Assert.Equal(s_now, result.Group!.FixedAt);
    }

    [IntegrationFact]
    public async Task Should_clear_only_explicitly_null_fields()
    {
        CrashGroup group = await SeedAsync();
        CrashTriageResult result = await Store.UpdateAsync(group.Id, new(5, Status: new(true, CrashGroupStatus.New), IssueUrl: new(true, null)), s_now, default);
        Assert.Equal(CrashTriageOutcome.Updated, result.Outcome);
        Assert.Null(result.Group!.IssueUrl);
        Assert.Equal("keep notes", result.Group.Notes);
        Assert.Equal("1.2.3", result.Group.FixedInVersion);
        result = await Store.UpdateAsync(group.Id, new(6, FixedInVersion: new(true, null), Notes: new(true, null)), s_now, default);
        Assert.Null(result.Group!.FixedInVersion);
        Assert.Null(result.Group.Notes);
        Assert.Equal(s_now, result.Group.FixedAt);
        Assert.Equal(1, (await LoadAsync()).ReportCount);
    }

    [IntegrationFact]
    public async Task Should_establish_a_new_baseline_for_an_explicit_fix_and_preserve_history()
    {
        CrashGroup group = await SeedAsync(regression: true);
        foreach (CrashGroupStatus status in new[] { CrashGroupStatus.New, CrashGroupStatus.Investigating, CrashGroupStatus.Fixed, CrashGroupStatus.Ignored })
        {
            CrashTriageResult result = await Store.UpdateAsync(group.Id, new(group.TriageRevision, Status: new(true, status)), s_now.AddMinutes(1), default);
            Assert.Equal(CrashTriageOutcome.Updated, result.Outcome);
            Assert.Equal(status is CrashGroupStatus.New or CrashGroupStatus.Investigating, result.Group!.IsRegression);
            Assert.Equal(2, result.Group.RegressionCount);
            Assert.Equal(s_now.AddDays(-1), result.Group.LastRegressionAt);
            Assert.Equal(status is CrashGroupStatus.Fixed or CrashGroupStatus.Ignored ? s_now.AddMinutes(1) : s_now, result.Group.FixedAt);
            group = await LoadAsync();
        }
        CrashTriageResult fixedAgain = await Store.UpdateAsync(group.Id, new(group.TriageRevision, Status: new(true, CrashGroupStatus.Fixed)), s_now.AddMinutes(2), default);
        CrashTriageResult reaffirmed = await Store.UpdateAsync(group.Id, new(fixedAgain.Group!.TriageRevision, Status: new(true, CrashGroupStatus.Fixed)), s_now.AddMinutes(3), default);
        Assert.Equal(s_now.AddMinutes(3), reaffirmed.Group!.FixedAt);
        CrashTriageResult changedVersion = await Store.UpdateAsync(group.Id, new(reaffirmed.Group.TriageRevision, FixedInVersion: new(true, "1.2.4")), s_now.AddMinutes(4), default);
        Assert.Equal(s_now.AddMinutes(4), changedVersion.Group!.FixedAt);
    }

    [IntegrationFact]
    public async Task Should_validate_the_result_atomically_and_distinguish_missing_and_conflict()
    {
        CrashGroup group = await SeedAsync();
        CrashGroupPatch[] invalid =
        [
            new(-1, Notes: new(true, "changed")), new(5), new(5, Status: new(true, null)),
            new(5, Status: new(true, (CrashGroupStatus)99)), new(5, FixedInVersion: new(true, null)),
            new(5, FixedInVersion: new(true, "01.2.3")), new(5, FixedInVersion: new(true, new string('a', 65))),
            new(5, Notes: new(true, new string('a', 4097)))
        ];
        foreach (CrashGroupPatch patch in invalid)
        {
            CrashTriageResult result = await Store.UpdateAsync(group.Id, patch, s_now, default);
            Assert.Equal(CrashTriageOutcome.Validation, result.Outcome);
            Assert.NotNull(result.ValidationCode);
        }
        Assert.Equal(group.Notes, (await LoadAsync()).Notes);
        Assert.Equal(5, (await LoadAsync()).TriageRevision);
        CrashTriageResult stale = await Store.UpdateAsync(group.Id, new(4, Notes: new(true, "lost")), s_now, default);
        Assert.Equal(CrashTriageOutcome.Conflict, stale.Outcome);
        Assert.Equal(5, stale.CurrentRevision);
        Assert.Null(stale.Group);
        Assert.Null(stale.ValidationCode);
        Assert.Equal(CrashTriageOutcome.Missing, (await Store.UpdateAsync(new(Guid.NewGuid()), new(0, Notes: new(true, "a")), s_now, default)).Outcome);
    }

    [IntegrationFact]
    public async Task Should_accept_only_exact_github_issue_urls_and_character_limits()
    {
        CrashGroup group = await SeedAsync();
        string[] invalid = ["http://github.com/o/r/issues/1", "https://github.com.evil/o/r/issues/1", "https://user@github.com/o/r/issues/1", "https://github.com/o/r/issues/1?q=1", "https://github.com/o/r/issues/1#x", "https://github.com/o/r/issues/0", "https://github.com/o/r/issues/-1", "https://github.com/o/r/issues/1/", "https://github.com/o/r/pull/1", "https://github.com:444/o/r/issues/1", "https://github.com/o/r/issues/1?", "https://github.com/o/r/issues/1#", "https://github.com/o%2Fr/r/issues/1", "https://github.com/o/r/issues/1\n", "https://github.com/o/r/ISSUES/1", "https://github.com/owner_/r/issues/1", "https://github.com/o/../issues/1", "https://github.com/" + new string('o', 2030) + "/r/issues/1"];
        foreach (string url in invalid)
        {
            CrashTriageResult result = await Store.UpdateAsync(group.Id, new(5, IssueUrl: new(true, url), Notes: new(true, "must not persist")), s_now, default);
            Assert.Equal(CrashTriageOutcome.Validation, result.Outcome);
            Assert.Equal(CrashTriageValidationCode.InvalidIssueUrl, result.ValidationCode);
        }
        const string Prefix = "HTTPS://GitHub.COM/owner/repo/issues/";
        string validUrl = Prefix + new string('1', 2048 - Prefix.Length);
        Assert.Equal(2048, validUrl.Length);
        CrashTriageResult valid = await Store.UpdateAsync(group.Id, new(5, FixedInVersion: new(true, "1.2.3+" + new string('a', 58)), IssueUrl: new(true, validUrl), Notes: new(true, new string('n', 4096))), s_now, default);
        Assert.Equal(CrashTriageOutcome.Updated, valid.Outcome);
        Assert.Equal(2048, valid.Group!.IssueUrl!.Length);
        Assert.Equal(4096, valid.Group.Notes!.Length);
        Assert.Equal(64, valid.Group.FixedInVersion!.Length);
    }

    [IntegrationFact]
    public async Task Should_ignore_delayed_pre_fix_reports()
    {
        await SeedAsync();
        await CompleteAsync(s_now.AddSeconds(-1), s_now.AddMinutes(1));
        CrashGroup group = await LoadAsync();
        Assert.Equal(CrashGroupStatus.Fixed, group.Status);
        Assert.Equal(5, group.TriageRevision);
        Assert.Equal(2, group.ReportCount);
        Assert.Equal(0, group.RegressionCount);
    }

    [IntegrationFact]
    public async Task Should_reopen_on_equal_fixed_version()
    {
        await SeedAsync();
        await CompleteAsync(s_now, s_now.AddMinutes(1));
        await CompleteAsync(s_now.AddSeconds(1), s_now.AddMinutes(2));
        CrashGroup group = await LoadAsync();
        Assert.Equal(CrashGroupStatus.New, group.Status);
        Assert.True(group.IsRegression);
        Assert.Equal(1, group.RegressionCount);
        Assert.Equal(6, group.TriageRevision);
        Assert.Equal(3, group.ReportCount);
        Assert.Equal(s_now.AddMinutes(1), group.LastRegressionAt);
        Assert.Equal("keep notes", group.Notes);
        Assert.Equal(s_now, group.FixedAt);
    }

    [IntegrationFact]
    public async Task Should_conflict_when_regression_reopens_during_an_edit()
    {
        CrashGroup original = await SeedAsync();
        CrashLease lease = await ClaimAsync(s_now.AddSeconds(1));
        var gate = new GroupLockBarrier();
        var queue = new CrashQueue(new BarrierFactory(_postgres.Factory, gate));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<bool> completion = queue.CompleteAsync(lease, Processed(), s_now.AddMinutes(1), timeout.Token);
        await gate.Acquired.Task.WaitAsync(timeout.Token);
        Task<CrashTriageResult> edit = Store.UpdateAsync(original.Id, new(5, Notes: new(true, "stale edit")), s_now.AddMinutes(2), timeout.Token);
        try { await WaitForBlockedWriterAsync(timeout.Token); }
        finally { gate.Release.TrySetResult(); }
        Assert.True(await completion);
        CrashTriageResult conflict = await edit;
        Assert.Equal(CrashTriageOutcome.Conflict, conflict.Outcome);
        Assert.Equal(6, conflict.CurrentRevision);
        Assert.Null(conflict.Group);
        CrashGroup group = await LoadAsync();
        Assert.Equal("keep notes", group.Notes);
        Assert.Equal(original.TriagedAt, group.TriagedAt);
        Assert.Equal(2, group.ReportCount);
        Assert.Equal(1, group.RegressionCount);
        Assert.Equal(CrashGroupStatus.New, group.Status);
        Assert.True(group.IsRegression);
        Assert.Equal(6, group.TriageRevision);
    }

    [IntegrationTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_preserve_an_edit_when_finalization_waits_for_triage(bool reaffirmFixed)
    {
        CrashGroup original = await SeedAsync();
        CrashLease lease = await ClaimAsync(s_now.AddSeconds(1));
        var gate = new GroupLockBarrier();
        var store = new CrashTriageStore(new BarrierFactory(_postgres.Factory, gate));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var patch = new CrashGroupPatch(5, Status: reaffirmFixed ? new(true, CrashGroupStatus.Fixed) : default, Notes: new(true, "saved edit"));
        Task<CrashTriageResult> edit = store.UpdateAsync(original.Id, patch, s_now.AddMinutes(1), timeout.Token);
        await gate.Acquired.Task.WaitAsync(timeout.Token);
        Assert.Equal(5, gate.CommandTimeout);
        Task<bool> completion = _postgres.Queue.CompleteAsync(lease, Processed(), s_now.AddMinutes(2), timeout.Token);
        try { await WaitForBlockedWriterAsync(timeout.Token); }
        finally { gate.Release.TrySetResult(); }
        Assert.Equal(CrashTriageOutcome.Updated, (await edit).Outcome);
        Assert.True(await completion);
        CrashGroup group = await LoadAsync();
        Assert.Equal("saved edit", group.Notes);
        Assert.Equal(reaffirmFixed ? s_now.AddMinutes(1) : s_now, group.FixedAt);
        Assert.Equal(s_now.AddMinutes(1), group.TriagedAt);
        Assert.Equal(2, group.ReportCount);
        Assert.Equal(reaffirmFixed ? 0 : 1, group.RegressionCount);
        Assert.Equal(reaffirmFixed ? 6 : 7, group.TriageRevision);
        Assert.Equal(reaffirmFixed ? CrashGroupStatus.Fixed : CrashGroupStatus.New, group.Status);
    }

    [IntegrationFact]
    public async Task Should_cancel_a_waiting_patch_without_changing_the_group()
    {
        CrashGroup original = await SeedAsync();
        await using CrashesDbContext blocker = _postgres.Factory.CreateDbContext();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Groups.FromSql($"SELECT * FROM crash_groups WHERE id = {original.Id.Value} FOR UPDATE").SingleAsync();
        using var cancellation = new CancellationTokenSource();
        using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<CrashTriageResult> edit = Store.UpdateAsync(original.Id, new(5, Notes: new(true, "canceled edit")), s_now, cancellation.Token);
        await WaitForBlockedWriterAsync(observation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => edit);
        await transaction.RollbackAsync();
        CrashGroup group = await LoadAsync();
        Assert.Equal(original.Notes, group.Notes);
        Assert.Equal(5, group.TriageRevision);
        Assert.Equal(1, group.ReportCount);
    }

    private async Task WaitForBlockedWriterAsync(CancellationToken ct)
    {
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        while (!await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database()
            AND wait_event_type = 'Lock' AND query LIKE '%crash_groups%' AND cardinality(pg_blocking_pids(pid)) > 0) AS "Value"
            """).SingleAsync(ct))
        {
            await Task.Yield();
        }
    }

    private async Task<CrashGroup> SeedAsync(bool regression = false)
    {
        CrashLease lease = await ClaimAsync(s_now.AddMinutes(-1));
        Assert.True(await _postgres.Queue.CompleteAsync(lease, Processed(), s_now, default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        CrashGroup group = await db.Groups.SingleAsync();
        group.Status = CrashGroupStatus.Fixed;
        group.FixedInVersion = "1.2.3";
        group.FixedAt = s_now;
        group.TriagedAt = s_now;
        group.TriageRevision = 5;
        group.Notes = "keep notes";
        group.IssueUrl = "https://github.com/o/r/issues/9";
        group.IsRegression = regression;
        group.RegressionCount = regression ? 2 : 0;
        group.LastRegressionAt = regression ? s_now.AddDays(-1) : null;
        await db.SaveChangesAsync();
        return group;
    }

    private async Task<CrashGroup> LoadAsync()
    {
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        return await db.Groups.AsNoTracking().SingleAsync();
    }

    private async Task<CrashLease> ClaimAsync(DateTimeOffset receivedAt)
    {
        var source = new StoredCrashObject($"1.2.3/2026-10-23/{Guid.NewGuid():N}.zip", "etag", 1024, receivedAt);
        Assert.True(await _postgres.Queue.DiscoverAsync(source, s_now, default));
        return Assert.IsType<CrashLease>(await _postgres.Queue.ClaimAsync(s_now, default));
    }

    private async Task CompleteAsync(DateTimeOffset receivedAt, DateTimeOffset processedAt)
        => Assert.True(await _postgres.Queue.CompleteAsync(await ClaimAsync(receivedAt), Processed(), processedAt, default));

    private static CrashAttemptResult Processed()
    {
        var analysis = new CrashAnalysis(1,
            new CrashIdentity("EXCEPTION_ACCESS_VIOLATION", 0xc0000005, "0x0000000000000010", 0, 42),
            new CrashSystem("Windows NT", "10.0.1", "1", "x86_64", null),
            [new CrashThread(0, 42, [new CrashFrame("runtime.exe", "0x0000000000000010", "Game::Run", null, null, [], "context") { ImageKey = "IMAGE" }])],
            [new CrashModule("runtime.exe", "IMAGE", "runtime.pdb", "PDB", "1.2.3", "0x0000000000000000", "0x0000000000001000", true)],
            new CrashMetadata(1, "runtime.exe", "1.2.3", "abc", 0xc0000005, "0x0000000000000010", 42, s_now),
            null, CrashQualityFlags.None, new ProcessorIdentity("0.27.0", "2.3.9", "v1"));
        return CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis));
    }

    private sealed class GroupLockBarrier : DbCommandInterceptor
    {
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CommandTimeout { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("crash_groups", StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                CommandTimeout = command.CommandTimeout;
                Acquired.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class BarrierFactory(CrashesDbContextFactory factory, GroupLockBarrier gate) : IDbContextFactory<CrashesDbContext>
    {
        public CrashesDbContext CreateDbContext()
        {
            using CrashesDbContext db = factory.CreateDbContext();
            return new CrashesDbContext(new DbContextOptionsBuilder<CrashesDbContext>().UseNpgsql(db.Database.GetConnectionString()).AddInterceptors(gate).Options);
        }
    }
}
