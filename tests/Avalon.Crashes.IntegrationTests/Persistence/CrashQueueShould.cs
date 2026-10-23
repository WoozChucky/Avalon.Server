using System.Text;
using System.Text.Json;
using Avalon.Crashes.Analysis;
using Avalon.Crashes.Persistence;
using Avalon.Database.Crashes;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Persistence;

public sealed class CrashQueueShould : IAsyncLifetime
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);
    private readonly PostgresFixture _postgres = new();

    public Task InitializeAsync() => _postgres.InitializeAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync();

    [IntegrationFact]
    public async Task Should_count_a_duplicate_key_once()
    {
        StoredCrashObject source = Source();
        bool[] discovered = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _postgres.Queue.DiscoverAsync(source, s_now, default)));
        Assert.Single(discovered, x => x);
        CrashLease?[] claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _postgres.Queue.ClaimAsync(s_now, default)));
        CrashLease lease = Assert.Single(claims.OfType<CrashLease>());
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _postgres.Queue.CompleteAsync(lease, Processed(), s_now, default)));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        Assert.Equal(1, await db.Reports.CountAsync());
        Assert.Equal(1L, (await db.Groups.SingleAsync()).ReportCount);
    }

    [IntegrationFact]
    public async Task Should_reject_stale_lease_completion()
    {
        CrashLease stale = await DiscoverAndClaimAsync();
        DateTimeOffset later = s_now.AddMinutes(11);
        CrashLease current = (await _postgres.Queue.ClaimAsync(later, default))!;
        Assert.NotEqual(stale.OwnerToken, current.OwnerToken);
        Assert.False(await _postgres.Queue.RenewAsync(stale, later, default));
        Assert.False(await _postgres.Queue.CompleteAsync(stale, Processed(), later, default));
        Assert.True(await _postgres.Queue.CompleteAsync(current, Processed(), later, default));
    }

    [IntegrationFact]
    public async Task Should_rollback_report_and_group_together()
    {
        CrashLease lease = await DiscoverAndClaimAsync();
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        // A real server-side failure after group mutation forces the whole transaction to roll back.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_crash_completion() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.status = 'processed' THEN RAISE EXCEPTION 'injected completion failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER fail_crash_completion BEFORE UPDATE ON crash_reports FOR EACH ROW EXECUTE FUNCTION fail_crash_completion();
            """);
        await Assert.ThrowsAnyAsync<Exception>(() => _postgres.Queue.CompleteAsync(lease, Processed(), s_now, default));
        Assert.Empty(await db.Groups.ToListAsync());
        Assert.Equal(CrashReportStatus.Processing, (await db.Reports.SingleAsync()).Status);
    }

    [IntegrationFact]
    public async Task Should_persist_attempts_before_external_work()
    {
        var queue = new Avalon.Database.Crashes.CrashQueue(_postgres.Factory, new ElapsedClock());
        CrashLease first = await DiscoverAndClaimAsync(queue: queue);
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        CrashReport report = await db.Reports.AsNoTracking().SingleAsync();
        Assert.Equal(1L, report.AttemptCount);
        Assert.Equal(s_now, report.FirstAttemptAt);
        Assert.Equal(s_now, report.RetryCampaignStartedAt);
        Assert.Equal(first.OwnerToken, report.LeaseOwner);
        Assert.Equal(s_now.AddMinutes(10), report.LeaseUntil);
        Assert.Null(report.Stack);

        for (int attempt = 1; attempt <= 2; attempt++)
            Assert.NotNull(await _postgres.Queue.ClaimAsync(s_now.AddMinutes(11 * attempt), default));
        Assert.Null(await _postgres.Queue.ClaimAsync(s_now.AddMinutes(33), default));
        report = await db.Reports.AsNoTracking().SingleAsync();
        Assert.Equal(3L, report.AttemptCount);
        Assert.Equal(3L, report.ConsecutiveToolFailures);
        Assert.Equal(CrashReportStatus.Failed, report.Status);
    }

    [IntegrationFact]
    public async Task Should_keep_dependency_retries_separate_from_tool_failures()
    {
        var queue = new Avalon.Database.Crashes.CrashQueue(_postgres.Factory, new ElapsedClock());
        CrashLease lease = await DiscoverAndClaimAsync(queue: queue);
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            DateTimeOffset now = s_now.AddMinutes(attempt);
            Assert.True(await queue.CompleteAsync(lease, CrashAttemptResult.Retry(CrashFailureCode.MissingRuntimeSymbols, now), now, default));
            lease = (await queue.ClaimAsync(now, default))!;
            Assert.Equal(0L, lease.ConsecutiveToolFailures);
        }
        Assert.Equal(6L, lease.AttemptCount);
        Assert.Equal(s_now, lease.FirstAttemptAt);
        Assert.Equal(s_now, lease.RetryCampaignStartedAt);
        Assert.True(await queue.CompleteAsync(lease, Processed(), s_now.AddMinutes(5), default));
    }

    [IntegrationFact]
    public async Task Should_reconcile_an_uncertain_commit()
    {
        CrashLease lease = await DiscoverAndClaimAsync();
        Assert.True(await _postgres.Queue.CompleteAsync(lease, Processed(), s_now, default));
        // Caller lost the first acknowledgement and replays with the same token.
        Assert.True(await _postgres.Queue.CompleteAsync(lease, Processed(), s_now.AddMinutes(1), default));
        Assert.Null(await _postgres.Queue.ClaimAsync(s_now.AddMinutes(11), default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        Assert.Equal(1L, (await db.Groups.SingleAsync()).ReportCount);
    }

    [IntegrationFact]
    public async Task Should_reopen_a_fixed_group_once()
    {
        CrashLease first = await DiscoverAndClaimAsync();
        await _postgres.Queue.CompleteAsync(first, Processed(), s_now, default);
        await using (CrashesDbContext db = _postgres.Factory.CreateDbContext())
        {
            CrashGroup group = await db.Groups.SingleAsync();
            group.Status = CrashGroupStatus.Fixed;
            group.FixedInVersion = "1.2.3";
            group.FixedAt = s_now.AddMinutes(1);
            group.TriageRevision = 4;
            await db.SaveChangesAsync();
        }
        DateTimeOffset received = s_now.AddMinutes(2);
        await _postgres.Queue.DiscoverAsync(Source(received), received, default);
        await _postgres.Queue.DiscoverAsync(Source(received), received, default);
        CrashLease[] leases = (await Task.WhenAll(_postgres.Queue.ClaimAsync(received, default), _postgres.Queue.ClaimAsync(received, default))).OfType<CrashLease>().ToArray();
        Assert.Equal(2, leases.Length);
        await Task.WhenAll(leases.Select(x => _postgres.Queue.CompleteAsync(x, Processed(), received, default)));
        await using CrashesDbContext verify = _postgres.Factory.CreateDbContext();
        CrashGroup reopened = await verify.Groups.SingleAsync();
        Assert.Equal(CrashGroupStatus.New, reopened.Status);
        Assert.True(reopened.IsRegression);
        Assert.Equal(1L, reopened.RegressionCount);
        Assert.Equal(5L, reopened.TriageRevision);
        Assert.Equal(3L, reopened.ReportCount);
        Assert.Equal("1.2.3", reopened.FixedInVersion);
    }

    [IntegrationFact]
    public async Task Should_update_group_extrema_by_received_time_and_uuid()
    {
        foreach ((DateTimeOffset received, string version) in new[] { (s_now.AddMinutes(2), "2.0.0"), (s_now, "1.0.0"), (s_now.AddMinutes(1), "3.0.0") })
        {
            await _postgres.Queue.DiscoverAsync(Source(received), s_now.AddMinutes(3), default);
            CrashLease lease = (await _postgres.Queue.ClaimAsync(s_now.AddMinutes(3), default))!;
            await _postgres.Queue.CompleteAsync(lease, Processed(version), s_now.AddMinutes(3), default);
        }
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        CrashGroup group = await db.Groups.SingleAsync();
        Assert.Equal(s_now, group.FirstSeen);
        Assert.Equal(s_now.AddMinutes(2), group.LastSeen);
        Assert.Equal("1.0.0", group.FirstVersion);
        Assert.Equal("2.0.0", group.LastVersion);
    }

    [IntegrationFact]
    public async Task Should_break_received_time_ties_by_lowest_uuid_in_reverse_arrival_order()
    {
        await _postgres.Queue.DiscoverAsync(Source(), s_now, default);
        await _postgres.Queue.DiscoverAsync(Source(), s_now, default);
        await using (CrashesDbContext db = _postgres.Factory.CreateDbContext())
        {
            CrashReport[] reports = await db.Reports.OrderBy(x => x.Id).ToArrayAsync();
            reports[0].NextAttemptAt = s_now.AddMinutes(1);
            reports[0].Status = CrashReportStatus.RetryWait;
            await db.SaveChangesAsync();
        }
        CrashLease higher = (await _postgres.Queue.ClaimAsync(s_now, default))!;
        Assert.True(await _postgres.Queue.CompleteAsync(higher, Processed("9.0.0"), s_now, default));
        CrashLease lower = (await _postgres.Queue.ClaimAsync(s_now.AddMinutes(1), default))!;
        Assert.True(lower.ReportId.Value.CompareTo(higher.ReportId.Value) < 0);
        Assert.True(await _postgres.Queue.CompleteAsync(lower, Processed("1.0.0"), s_now.AddMinutes(1), default));
        await using CrashesDbContext verify = _postgres.Factory.CreateDbContext();
        CrashGroup group = await verify.Groups.SingleAsync();
        Assert.Equal(lower.ReportId.Value, group.FirstReportId);
        Assert.Equal(lower.ReportId.Value, group.LastReportId);
        Assert.Equal("1.0.0", group.FirstVersion);
        Assert.Equal("1.0.0", group.LastVersion);
    }

    [IntegrationFact]
    public async Task Should_skip_a_locked_report_and_renew_the_current_lease()
    {
        await _postgres.Queue.DiscoverAsync(Source(), s_now, default);
        await _postgres.Queue.DiscoverAsync(Source(), s_now, default);
        await using CrashesDbContext locked = _postgres.Factory.CreateDbContext();
        await using IDbContextTransaction transaction = await locked.Database.BeginTransactionAsync();
        CrashReport held = await locked.Reports.FromSqlRaw("SELECT * FROM crash_reports ORDER BY id LIMIT 1 FOR UPDATE").SingleAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        CrashLease lease = (await _postgres.Queue.ClaimAsync(s_now, timeout.Token))!;
        Assert.NotEqual(held.Id, lease.ReportId);
        Assert.True(await _postgres.Queue.RenewAsync(lease, s_now.AddMinutes(9), default));
        Assert.True(await _postgres.Queue.CompleteAsync(lease, Processed(), s_now.AddMinutes(11), default));
    }

    [IntegrationFact]
    public async Task Should_bound_tool_failures_and_the_dependency_retry_campaign()
    {
        CrashLease lease = await DiscoverAndClaimAsync();
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            DateTimeOffset now = s_now.AddMinutes(attempt);
            Assert.True(await _postgres.Queue.CompleteAsync(lease, CrashAttemptResult.Retry(CrashFailureCode.ToolTimeout, now), now, default));
            CrashLease? next = await _postgres.Queue.ClaimAsync(now, default);
            if (attempt < 3)
                lease = Assert.IsType<CrashLease>(next);
            else
                Assert.Null(next);
        }
        await using (CrashesDbContext db = _postgres.Factory.CreateDbContext())
        {
            CrashReport failed = await db.Reports.SingleAsync();
            Assert.Equal(3L, failed.AttemptCount);
            Assert.Equal(3L, failed.ConsecutiveToolFailures);
            Assert.Equal(CrashReportStatus.Failed, failed.Status);
            Assert.Null(failed.Meta);
            Assert.Null(failed.Stack);
            Assert.Null(failed.LogTail);
            Assert.Empty(await db.Groups.ToListAsync());
        }

        CrashLease dependency = await DiscoverAndClaimAsync();
        Assert.True(await _postgres.Queue.CompleteAsync(dependency, CrashAttemptResult.Retry(CrashFailureCode.StorageUnavailable, s_now.AddHours(23)), s_now, default));
        Assert.Null(await _postgres.Queue.ClaimAsync(dependency.RetryCampaignStartedAt.AddHours(24), default));
        await using CrashesDbContext verify = _postgres.Factory.CreateDbContext();
        CrashReport stopped = await verify.Reports.SingleAsync(x => x.Id == dependency.ReportId);
        Assert.Equal(CrashReportStatus.Failed, stopped.Status);
        Assert.Equal(1L, stopped.AttemptCount);
        Assert.Equal(0L, stopped.ConsecutiveToolFailures);
    }

    [IntegrationFact]
    public async Task Should_store_quality_flags_as_normalized_enum_names()
    {
        CrashLease lease = await DiscoverAndClaimAsync();
        CrashAnalysis analysis = Analysis() with { Quality = CrashQualityFlags.ScanFrames | CrashQualityFlags.Truncated };
        Assert.True(await _postgres.Queue.CompleteAsync(lease, CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis)), s_now, default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        string stored = await db.Database.SqlQueryRaw<string>("SELECT quality_flags::text AS \"Value\" FROM crash_reports").SingleAsync();
        Assert.Equal("scanFrames, truncated", stored);
        Assert.Equal(analysis.Quality, (await db.Reports.SingleAsync()).QualityFlags);
    }

    [IntegrationFact]
    public async Task Should_discard_completion_that_expires_while_waiting_for_a_group_lock()
    {
        CrashLease initial = await DiscoverAndClaimAsync();
        await _postgres.Queue.CompleteAsync(initial, Processed(), s_now, default);
        CrashLease expiring = await DiscoverAndClaimAsync(Source(s_now.AddDays(-90).AddMinutes(1)));
        await using CrashesDbContext held = _postgres.Factory.CreateDbContext();
        await using IDbContextTransaction transaction = await held.Database.BeginTransactionAsync();
        await held.Groups.FromSqlRaw("SELECT * FROM crash_groups FOR UPDATE").SingleAsync();
        var clock = new ElapsedClock();
        var queue = new Avalon.Database.Crashes.CrashQueue(_postgres.Factory, clock);
        Task<bool> completion = queue.CompleteAsync(expiring, Processed(), s_now, default);
        await using CrashesDbContext observer = _postgres.Factory.CreateDbContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await observer.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database()
            AND wait_event_type = 'Lock' AND query LIKE '%crash_groups%') AS "Value"
            """).SingleAsync(timeout.Token))
        {
            await Task.Delay(10, timeout.Token);
        }
        clock.Advance(TimeSpan.FromMinutes(2));
        await transaction.CommitAsync();
        Assert.False(await completion);
        Assert.Equal(1L, (await observer.Groups.SingleAsync()).ReportCount);
        Assert.Null((await observer.Reports.SingleAsync(x => x.Id == expiring.ReportId)).Stack);
    }

    [IntegrationFact]
    public async Task Should_not_discover_an_object_that_expires_while_waiting_for_the_database()
    {
        await using CrashesDbContext held = _postgres.Factory.CreateDbContext();
        await using IDbContextTransaction transaction = await held.Database.BeginTransactionAsync();
        await held.Database.ExecuteSqlRawAsync("LOCK TABLE crash_reports IN ACCESS EXCLUSIVE MODE");
        var clock = new ElapsedClock();
        var queue = new Avalon.Database.Crashes.CrashQueue(_postgres.Factory, clock);
        Task<bool> discovery = queue.DiscoverAsync(Source(s_now.AddDays(-90).AddMinutes(1)), s_now, default);
        await WaitForReportLockAsync();
        clock.Advance(TimeSpan.FromMinutes(2));
        await transaction.CommitAsync();
        Assert.False(await discovery);
        await using CrashesDbContext verify = _postgres.Factory.CreateDbContext();
        Assert.Empty(await verify.Reports.ToListAsync());
    }

    [IntegrationFact]
    public async Task Should_not_claim_a_report_that_expires_while_waiting_for_the_database()
    {
        await _postgres.Queue.DiscoverAsync(Source(s_now.AddDays(-90).AddMinutes(1)), s_now, default);
        await using CrashesDbContext held = _postgres.Factory.CreateDbContext();
        await using IDbContextTransaction transaction = await held.Database.BeginTransactionAsync();
        await held.Database.ExecuteSqlRawAsync("LOCK TABLE crash_reports IN ACCESS EXCLUSIVE MODE");
        var clock = new ElapsedClock();
        var queue = new Avalon.Database.Crashes.CrashQueue(_postgres.Factory, clock);
        Task<CrashLease?> claim = queue.ClaimAsync(s_now, default);
        await WaitForReportLockAsync();
        clock.Advance(TimeSpan.FromMinutes(2));
        await transaction.CommitAsync();
        Assert.Null(await claim);
        await using CrashesDbContext verify = _postgres.Factory.CreateDbContext();
        CrashReport report = await verify.Reports.SingleAsync();
        Assert.Equal(CrashReportStatus.Pending, report.Status);
        Assert.Equal(0L, report.AttemptCount);
        Assert.Null(report.LeaseOwner);
    }

    [IntegrationFact]
    public async Task Should_refresh_claim_time_after_draining_an_exhausted_report()
    {
        await _postgres.Queue.DiscoverAsync(Source(s_now.AddDays(-3)), s_now.AddMinutes(-33), default);
        for (int attempt = 0; attempt < 3; attempt++)
            Assert.NotNull(await _postgres.Queue.ClaimAsync(s_now.AddMinutes(-33 + attempt * 11), default));

        await using CrashesDbContext held = _postgres.Factory.CreateDbContext();
        await held.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION block_exhausted_crash() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.status = 'failed' THEN PERFORM pg_advisory_xact_lock(71941); END IF; RETURN NEW; END $$;
            CREATE TRIGGER block_exhausted_crash BEFORE UPDATE ON crash_reports FOR EACH ROW EXECUTE FUNCTION block_exhausted_crash();
            """);
        await using IDbContextTransaction transaction = await held.Database.BeginTransactionAsync();
        await held.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71941)");
        var clock = new ElapsedClock();
        var queue = new Avalon.Database.Crashes.CrashQueue(_postgres.Factory, clock);
        Task<CrashLease?> claim = queue.ClaimAsync(s_now, default);
        await WaitForReportLockAsync();
        StoredCrashObject expiring = Source(s_now.AddDays(-90).AddMinutes(1));
        Assert.True(await _postgres.Queue.DiscoverAsync(expiring, s_now, default));
        clock.Advance(TimeSpan.FromMinutes(2));
        await transaction.CommitAsync();
        Assert.Null(await claim);
        await using CrashesDbContext verify = _postgres.Factory.CreateDbContext();
        CrashReport untouched = await verify.Reports.SingleAsync(x => x.ObjectKey == expiring.Key);
        Assert.Equal(CrashReportStatus.Pending, untouched.Status);
        Assert.Equal(0L, untouched.AttemptCount);
        Assert.Equal(1, await verify.Reports.CountAsync(x => x.Status == CrashReportStatus.Failed));
    }

    [IntegrationFact]
    public async Task Should_infer_a_loaded_driver_only_when_the_known_set_is_unambiguous()
    {
        CrashModule[] drivers =
        [
            new("nvwgf2umx.dll", "NVIDIA", null, null, "nvidia-version", null, null, false),
            new("atidxx64.dll", "AMD", null, null, "amd-version", null, null, false)
        ];
        foreach (int count in new[] { 0, 1, 2 })
        {
            CrashLease lease = await DiscoverAndClaimAsync();
            CrashAnalysis analysis = Analysis() with { Modules = Analysis().Modules.Concat(drivers.Take(count)).ToArray() };
            Assert.True(await _postgres.Queue.CompleteAsync(lease, CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis)), s_now, default));
            await using CrashesDbContext verify = _postgres.Factory.CreateDbContext();
            CrashReport report = await verify.Reports.SingleAsync(x => x.Id == lease.ReportId);
            Assert.Equal(count == 1 ? "nvwgf2umx.dll" : null, report.GpuDriverModule);
            Assert.Equal(count == 1 ? "nvidia-version" : null, report.GpuDriverVersion);
            Assert.Equal(count == 1 ? "loaded_module" : "unknown", report.GpuDriverSource);
        }
    }

    [IntegrationFact]
    public async Task Should_bound_compact_analysis_without_rejecting_jsonb_formatting_overhead()
    {
        CrashLease lease = await DiscoverAndClaimAsync();
        var frame = new CrashFrame("runtime.exe", "0x0000000000000010", new string('A', 1024), null, null, [], "context") { ImageKey = "IMAGE" };
        CrashAnalysis WithFrameCount(int count) => Analysis() with
        {
            LogTail = null,
            Threads = Enumerable.Repeat(frame, count).Chunk(256).Select((frames, index) => new CrashThread(index, 42, frames)).ToArray()
        };
        int lower = 1;
        int upper = 2048;
        while (lower < upper)
        {
            int middle = (lower + upper + 1) / 2;
            if (JsonSerializer.SerializeToUtf8Bytes(WithFrameCount(middle)).Length <= CrashLimits.StackBytes)
                lower = middle;
            else
                upper = middle - 1;
        }
        CrashAnalysis valid = WithFrameCount(lower);
        SignatureIdentity signature = CrashSignature.Create(valid);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(valid).Length, CrashLimits.StackBytes - 2048, CrashLimits.StackBytes);
        CrashInputException error = await Assert.ThrowsAsync<CrashInputException>(() => _postgres.Queue.CompleteAsync(lease, CrashAttemptResult.Processed(WithFrameCount(lower + 1), signature), s_now, default));
        Assert.Equal(CrashFailureCode.InvalidAnalysis, error.Code);
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        Assert.Empty(await db.Groups.ToListAsync());
        Assert.Null((await db.Reports.AsNoTracking().SingleAsync()).Stack);
        Assert.True(await _postgres.Queue.CompleteAsync(lease, CrashAttemptResult.Processed(valid, signature), s_now, default));
        CrashReport stored = await db.Reports.AsNoTracking().SingleAsync();
        Assert.NotNull(stored.Stack);
        Assert.True(Encoding.UTF8.GetByteCount(stored.Stack) > CrashLimits.StackBytes);
        Assert.Equal(lower, JsonSerializer.Deserialize<CrashAnalysis>(stored.Stack)!.Threads.Sum(x => x.Frames.Count));
    }

    [IntegrationFact]
    public async Task Should_refuse_a_canonical_hash_collision()
    {
        CrashLease first = await DiscoverAndClaimAsync();
        CrashAttemptResult result = Processed();
        await _postgres.Queue.CompleteAsync(first, result, s_now, default);
        CrashLease second = await DiscoverAndClaimAsync();
        SignatureIdentity collision = result.Signature! with { Canonical = "[\"different\"]" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _postgres.Queue.CompleteAsync(second, CrashAttemptResult.Processed(result.Analysis!, collision), s_now, default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        Assert.Equal(1L, (await db.Groups.SingleAsync()).ReportCount);
        Assert.Equal(1, await db.Reports.CountAsync(x => x.Status == CrashReportStatus.Processed));
    }

    [IntegrationFact]
    public async Task Should_enforce_expiry_and_lease_deadline_without_cleanup()
    {
        Assert.False(await _postgres.Queue.DiscoverAsync(Source(s_now.AddDays(-90)), s_now, default));
        CrashLease lease = await DiscoverAndClaimAsync(Source(s_now.AddDays(-90).AddMinutes(1)));
        Assert.False(await _postgres.Queue.RenewAsync(lease, s_now.AddMinutes(1), default));
        Assert.False(await _postgres.Queue.CompleteAsync(lease, Processed(), s_now.AddMinutes(1), default));
        Assert.Null(await _postgres.Queue.ClaimAsync(s_now.AddMinutes(1), default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        Assert.Empty(await db.Groups.ToListAsync());
        Assert.Null((await db.Reports.SingleAsync()).Stack);
        CrashLease unexpiredReport = await DiscoverAndClaimAsync();
        Assert.False(await _postgres.Queue.RenewAsync(unexpiredReport, s_now.AddMinutes(10), default));
        Assert.False(await _postgres.Queue.CompleteAsync(unexpiredReport, Processed(), s_now.AddMinutes(10), default));
    }

    [IntegrationFact]
    public async Task Should_preserve_source_fingerprint_and_reject_a_changed_object()
    {
        StoredCrashObject source = Source();
        CrashLease lease = await DiscoverAndClaimAsync(source);
        Assert.False(await _postgres.Queue.DiscoverAsync(source with { ETag = "replacement", LastModified = s_now.AddDays(1) }, s_now.AddDays(1), default));
        Assert.False(await _postgres.Queue.CompleteAsync(lease, Processed(), s_now.AddDays(1), default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        CrashReport report = await db.Reports.SingleAsync();
        Assert.Equal(source.ETag, report.SourceETag);
        Assert.Equal(source.LastModified, report.ReceivedAt);
        Assert.Equal(source.LastModified.AddDays(90), report.ExpiresAt);
        Assert.Equal(CrashReportStatus.Rejected, report.Status);
        Assert.Equal(CrashFailureCode.ObjectChanged, report.ReasonCode);
        Assert.Null(report.Meta);
        Assert.Null(report.Stack);
    }

    [IntegrationFact]
    public async Task Should_identify_the_faulting_image_from_the_original_thread_index()
    {
        CrashLease lease = await DiscoverAndClaimAsync();
        CrashAnalysis analysis = Analysis() with
        {
            Crash = new CrashIdentity("EXCEPTION_ACCESS_VIOLATION", 0xc0000005, "0x0000000000000000", 9, 42),
            Threads = [new CrashThread(9, 42, [new CrashFrame("nvwgf2umx.dll", "0x0000000000000010", "Driver", null, null, [], "context") { ImageKey = "RIGHT" }])],
            Modules = [new CrashModule("atidxx64.dll", "AMD", null, null, "amd-version", null, null, false), new CrashModule("nvwgf2umx.dll", "WRONG", null, null, "old", null, null, false), new CrashModule("nvwgf2umx.dll", "RIGHT", null, null, "new", null, null, false)]
        };
        Assert.True(await _postgres.Queue.CompleteAsync(lease, CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis)), s_now, default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        CrashReport report = await db.Reports.SingleAsync();
        Assert.Equal("nvwgf2umx.dll", report.CrashingModule);
        Assert.Equal("new", report.GpuDriverVersion);
        Assert.Equal("faulting_module", report.GpuDriverSource);
    }

    private async Task WaitForReportLockAsync()
    {
        await using CrashesDbContext observer = _postgres.Factory.CreateDbContext();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await observer.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database()
            AND wait_event_type = 'Lock' AND query LIKE '%crash_reports%') AS "Value"
            """).SingleAsync(timeout.Token))
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private async Task<CrashLease> DiscoverAndClaimAsync(StoredCrashObject? source = null, ICrashQueue? queue = null)
    {
        queue ??= _postgres.Queue;
        Assert.True(await queue.DiscoverAsync(source ?? Source(), s_now, default));
        return Assert.IsType<CrashLease>(await queue.ClaimAsync(s_now, default));
    }

    private static StoredCrashObject Source(DateTimeOffset? received = null) => new($"1.2.3/2026-10-23/{Guid.NewGuid():N}.zip", "etag", 1024, received ?? s_now);
    private static CrashAttemptResult Processed(string version = "1.2.3")
    {
        CrashAnalysis analysis = Analysis(version);
        return CrashAttemptResult.Processed(analysis, CrashSignature.Create(analysis));
    }

    private static CrashAnalysis Analysis(string version = "1.2.3") => new(1,
        new CrashIdentity("EXCEPTION_ACCESS_VIOLATION", 0xc0000005, "0x0000000000000010", 0, 42),
        new CrashSystem("Windows NT", "10.0.1", "1", "x86_64", null),
        [new CrashThread(0, 42, [new CrashFrame("runtime.exe", "0x0000000000000010", "Game::Run", null, null, [], "context") { ImageKey = "IMAGE" }])],
        [new CrashModule("runtime.exe", "IMAGE", "runtime.pdb", "PDB", version, "0x0000000000000000", "0x0000000000001000", true)],
        new CrashMetadata(1, "runtime.exe", version, "abc", 0xc0000005, "0x0000000000000010", 42, s_now),
        "safe log", CrashQualityFlags.None, new ProcessorIdentity("0.27.0", "2.3.9", "v1"));

    private sealed class ElapsedClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }
}
