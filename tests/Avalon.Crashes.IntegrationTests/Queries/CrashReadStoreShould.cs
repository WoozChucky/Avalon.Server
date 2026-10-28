using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Avalon.Crashes.Queries;
using Avalon.Database.Crashes;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Queries;

[Trait("Category", "CrashesPostgres")]
public sealed class CrashReadStoreShould : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private static readonly DateTimeOffset s_now = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
    private CrashReadStore Read => new(_postgres.Factory);
    public Task InitializeAsync() => _postgres.InitializeAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync();

    [IntegrationFact]
    public async Task Should_keep_lifetime_counts_after_all_details_expire()
    {
        CrashGroup group = Group();
        group.ReportCount = 17;
        CrashReport expired = Report(group, s_now.AddDays(-90));
        await SeedAsync(group, expired);
        CrashGroupPage page = await Read.ListGroupsAsync(new(), s_now, default);
        Assert.Equal(1, page.Total);
        Assert.Equal(17L, Assert.Single(page.Items).ReportCount);
        CrashGroupDetail detail = Assert.IsType<CrashGroupDetail>(await Read.GetGroupAsync(group.Id, 1, 25, s_now, default));
        Assert.Equal(17L, detail.Group.ReportCount);
        Assert.Equal(0L, detail.RetainedReportCount);
        Assert.Null(detail.LatestReportId);
        Assert.Null(detail.LatestStack);
        Assert.Empty(detail.ByVersion);
        Assert.Empty(detail.ByGpuDriver);
        Assert.Empty(detail.ByOsBuild);
        Assert.Empty(detail.Reports.Items);
        Assert.Null(await Read.GetReportAsync(expired.Id, s_now, default));
        Assert.Null(await Read.GetGroupAsync(new(Guid.NewGuid()), 1, 25, s_now, default));
        Assert.Null(await Read.GetReportAsync(new(Guid.NewGuid()), s_now, default));
    }

    [IntegrationFact]
    public async Task Should_filter_by_matching_reports_not_group_version_extrema()
    {
        CrashGroup group = Group();
        group.FirstVersion = "1.0.0";
        group.LastVersion = "3.0.0";
        await SeedAsync(group, Report(group, s_now.AddDays(-4), "2.0.0"), Report(group, s_now.AddDays(-1), "3.0.0"));
        CrashGroupPage page = await Read.ListGroupsAsync(new(Version: "2.0.0"), s_now, default);
        CrashGroupSummary item = Assert.Single(page.Items);
        Assert.Equal(2, item.RetainedReportCount);
        Assert.Equal(1, item.MatchingReportCount);
        Assert.Equal(1, item.DailyCounts.Sum(x => x.Count));
        Assert.Empty((await Read.ListGroupsAsync(new(Version: "1.0.0"), s_now, default)).Items);
        Assert.Empty((await Read.ListGroupsAsync(new(Version: "2.0.0", Since: s_now.AddDays(-2)), s_now, default)).Items);
        Assert.Empty((await Read.ListGroupsAsync(new(Status: CrashGroupStatus.Fixed, Version: "2.0.0"), s_now, default)).Items);
        Assert.Single((await Read.ListGroupsAsync(new(Status: CrashGroupStatus.New, Version: "2.0.0", Since: s_now.AddDays(-4)), s_now, default)).Items);
        CrashGroupPage old = await Read.ListGroupsAsync(new(Since: s_now.AddYears(-5)), s_now, default);
        Assert.Equal(s_now.AddDays(-90), old.EffectiveSince);
        Assert.Single(old.Items);
    }

    [IntegrationFact]
    public async Task Should_zero_fill_thirty_utc_days()
    {
        CrashGroup group = Group();
        await SeedAsync(group, Report(group, s_now.AddTicks(-10)), Report(group, s_now), Report(group, s_now.AddDays(-29)), Report(group, s_now.AddDays(-30)));
        DateTimeOffset offsetNow = s_now.ToOffset(TimeSpan.FromHours(-7));
        CrashGroupPage page = await Read.ListGroupsAsync(new(), offsetNow, default);
        IReadOnlyList<CrashDailyCount> counts = Assert.Single(page.Items).DailyCounts;
        Assert.Equal(30, counts.Count);
        Assert.Equal(new DateOnly(2026, 9, 25), counts[0].Date);
        Assert.Equal(1, counts[0].Count);
        Assert.Equal(new DateOnly(2026, 10, 24), counts[29].Date);
        Assert.Equal(1, counts[29].Count);
        Assert.Equal(1, counts[28].Count);
        Assert.Equal(0, counts[27].Count);
        Assert.Equal(3, counts.Sum(x => x.Count));
        Assert.Equal(TimeSpan.Zero, page.AsOf.Offset);
        CrashGroupPage since = await Read.ListGroupsAsync(new(Since: s_now), offsetNow, default);
        Assert.Equal(1, Assert.Single(since.Items).DailyCounts.Sum(x => x.Count));
    }

    [IntegrationFact]
    public async Task Should_not_count_one_report_for_multiple_drivers()
    {
        CrashGroup group = Group();
        CrashReport known = Report(group, s_now);
        known.GpuDriverModule = "nvwgf2umx.dll";
        known.GpuDriverVersion = "42";
        known.GpuDriverSource = "faulting_module";
        known.OsBuild = "26100";
        known.GpuName = "NVIDIA GeForce RTX 4090";
        known.VulkanDriverVersion = "581.57";
        CrashReport unknown = Report(group, s_now.AddHours(-1));
        unknown.Version = null;
        await SeedAsync(group, known, unknown);
        CrashGroupDetail detail = (await Read.GetGroupAsync(group.Id, 1, 25, s_now, default))!;
        Assert.Equal(2, detail.RetainedReportCount);
        Assert.Equal(detail.RetainedReportCount, detail.ByGpuDriver.Sum(x => x.Count));
        Assert.Equal(detail.RetainedReportCount, detail.ByOsBuild.Sum(x => x.Count));
        Assert.Equal(detail.RetainedReportCount, detail.ByVersion.Sum(x => x.Count));
        Assert.Contains(new CrashGpuDriverCount("unknown", "unknown", "unknown", 1), detail.ByGpuDriver);
        Assert.Contains(new CrashGpuDriverCount("nvwgf2umx.dll", "42", "faulting_module", 1), detail.ByGpuDriver);
        Assert.Contains(new CrashVersionCount("unknown", 1), detail.ByVersion);
        Assert.Contains(new CrashOsBuildCount("unknown", 1), detail.ByOsBuild);
        // The client's GPU (schema 2, Client#459): a report from before it, or without it, reads as unknown.
        Assert.Equal(detail.RetainedReportCount, detail.ByGpu.Sum(x => x.Count));
        Assert.Contains(new CrashGpuCount("NVIDIA GeForce RTX 4090", "581.57", 1), detail.ByGpu);
        Assert.Contains(new CrashGpuCount("unknown", "unknown", 1), detail.ByGpu);
    }

    [IntegrationFact]
    public async Task Should_return_the_newest_unexpired_stack_independent_of_report_page()
    {
        CrashGroup group = Group();
        CrashReport newest = Report(group, s_now);
        newest.Id = new(new Guid("00000000-0000-0000-0000-000000000001"));
        CrashReport tie = Report(group, s_now);
        tie.Id = new(new Guid("00000000-0000-0000-0000-000000000002"));
        await SeedAsync(group, newest, tie, Report(group, s_now.AddDays(-90)));
        CrashGroupDetail detail = (await Read.GetGroupAsync(group.Id, 2, 1, s_now, default))!;
        Assert.Equal(newest.Id, detail.LatestReportId);
        Assert.Equal(s_now, detail.LatestStackReceivedAt);
        Assert.Equal(7, detail.LatestStack!.Index);
        Assert.Equal("Fault", Assert.Single(detail.LatestStack.Frames).Function);
        Assert.Equal(CrashQualityFlags.ScanFrames, detail.LatestStackQuality);
        Assert.Equal(tie.Id, Assert.Single(detail.Reports.Items).Id);
        Assert.Equal(2, detail.Reports.Total);
        string serialized = JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("private log", serialized);
        Assert.DoesNotContain("private-hash", serialized);
        Assert.DoesNotContain("private metadata", serialized);
        Assert.DoesNotContain("private-key", serialized);
        CrashReportDetail report = (await Read.GetReportAsync(newest.Id, s_now, default))!;
        Assert.Equal("private log", report.LogTail);
        Assert.Equal(0xc0000005u, report.ExceptionCode);
        Assert.Equal("private metadata", report.Analysis!.Metadata.GitHash);
        Assert.Equal(2, report.Analysis.Threads.Count);
        Assert.Equal(2, report.Analysis.Modules.Count);
        Assert.DoesNotContain("private-key", JsonSerializer.Serialize(report));
    }

    [IntegrationFact]
    public async Task Should_order_regressions_new_and_other_groups_with_stable_id_ties()
    {
        CrashGroup other = Group();
        other.Status = CrashGroupStatus.Fixed;
        CrashGroup second = Group();
        second.Id = new(new Guid("00000000-0000-0000-0000-000000000002"));
        CrashGroup first = Group();
        first.Id = new(new Guid("00000000-0000-0000-0000-000000000001"));
        CrashGroup regression = Group();
        regression.IsRegression = true;
        regression.LastSeen = s_now.AddDays(-1);
        CrashGroup olderNew = Group();
        olderNew.Id = new(Guid.Empty);
        olderNew.LastSeen = s_now.AddDays(-1);
        await SeedAsync(olderNew);
        await SeedAsync(other);
        await SeedAsync(second);
        await SeedAsync(first);
        await SeedAsync(regression);
        CrashGroupPage page = await Read.ListGroupsAsync(new(PageSize: 2), s_now, default);
        Assert.Equal(5, page.Total);
        Assert.Equal(new[] { regression.Id, first.Id }, page.Items.Select(x => x.Id));
        Assert.Equal(new[] { second.Id, olderNew.Id }, (await Read.ListGroupsAsync(new(Page: 2, PageSize: 2), s_now, default)).Items.Select(x => x.Id));
        Assert.Equal(other.Id, Assert.Single((await Read.ListGroupsAsync(new(Page: 3, PageSize: 2), s_now, default)).Items).Id);
        Assert.Empty((await Read.ListGroupsAsync(new(Page: 1000, PageSize: 100), s_now, default)).Items);
    }

    [IntegrationFact]
    public async Task Should_exclude_failed_rejected_and_pending_rows_from_counts_and_return_only_safe_envelopes()
    {
        CrashGroup group = Group();
        await SeedAsync(group, Report(group, s_now));
        foreach (CrashReportStatus status in new[] { CrashReportStatus.Failed, CrashReportStatus.Rejected, CrashReportStatus.Pending })
        {
            CrashReport row = Report(group, s_now);
            row.Status = status;
            row.GroupId = null;
            row.Stack = null;
            row.ProcessedAt = null;
            row.SignatureVersion = null;
            row.Version = null;
            row.GitHash = null;
            row.LogTail = null;
            row.ReasonCode = CrashFailureCode.ToolFailed;
            row.ReasonDetail = "private diagnostic";
            await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
            db.Reports.Add(row);
            await db.SaveChangesAsync();
            CrashReportDetail detail = (await Read.GetReportAsync(row.Id, s_now, default))!;
            Assert.Equal(status, detail.Status);
            Assert.Equal(CrashFailureCode.ToolFailed, detail.Reason);
            Assert.Null(detail.Analysis);
            Assert.Null(detail.ExceptionCode);
            Assert.Null(detail.Cpu);
            Assert.DoesNotContain("private", JsonSerializer.Serialize(detail));
        }
        Assert.Equal(1, Assert.Single((await Read.ListGroupsAsync(new(), s_now, default)).Items).RetainedReportCount);
        Assert.Equal(1, (await Read.GetGroupAsync(group.Id, 1, 25, s_now, default))!.Reports.Total);
    }

    [IntegrationFact]
    public async Task Should_validate_filter_and_pagination_before_reading()
    {
        foreach (CrashGroupFilter filter in new[] { new CrashGroupFilter(Page: 0), new(Page: 1001), new(PageSize: 0), new(PageSize: 101), new(Since: s_now.AddTicks(1)), new(Version: "../unsafe"), new(Status: (CrashGroupStatus)99) })
            await Assert.ThrowsAnyAsync<ArgumentException>(() => Read.ListGroupsAsync(filter, s_now, default));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Read.GetGroupAsync(new(Guid.NewGuid()), 0, 25, s_now, default));
    }

    [IntegrationFact]
    public async Task Should_keep_page_and_total_in_one_snapshot_during_concurrent_insert()
    {
        CrashGroup original = Group();
        await SeedAsync(original, Report(original, s_now));
        var probe = new QueryProbe { BeforeSecondRead = () => SeedAsync(Group()) };
        CrashReadStore read = await ProbedReadAsync(probe);
        CrashGroupPage page = await read.ListGroupsAsync(new(), s_now, default);
        Assert.Equal(1, page.Total);
        Assert.Equal(original.Id, Assert.Single(page.Items).Id);
        Assert.All(probe.Queries, x => Assert.Equal(IsolationLevel.RepeatableRead, x.Isolation));
        Assert.All(probe.Queries, x => Assert.Equal(5, x.Timeout));
        Assert.Equal(2, (await Read.ListGroupsAsync(new(), s_now, default)).Total);
    }

    [IntegrationFact]
    public async Task Should_keep_group_counts_breakdowns_and_latest_in_one_snapshot_during_concurrent_delete()
    {
        CrashGroup group = Group();
        CrashReport original = Report(group, s_now);
        await SeedAsync(group, original);
        var probe = new QueryProbe
        {
            BeforeSecondRead = async () =>
            {
                await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
                await db.Reports.ExecuteDeleteAsync();
                await db.Groups.Where(x => x.Id == group.Id).ExecuteUpdateAsync(x => x.SetProperty(g => g.Notes, "changed"));
            }
        };
        CrashReadStore read = await ProbedReadAsync(probe);
        CrashGroupDetail detail = (await read.GetGroupAsync(group.Id, 1, 25, s_now, default))!;
        Assert.Equal(1, detail.RetainedReportCount);
        Assert.Equal(original.Id, detail.LatestReportId);
        Assert.Equal(original.Id, Assert.Single(detail.Reports.Items).Id);
        Assert.Equal(1, Assert.Single(detail.ByVersion).Count);
        Assert.Equal("private note", detail.Notes);
        Assert.All(probe.Queries, x => Assert.Equal(IsolationLevel.RepeatableRead, x.Isolation));
        Assert.All(probe.Queries, x => Assert.Equal(5, x.Timeout));
        Assert.Equal(0, (await Read.GetGroupAsync(group.Id, 1, 25, s_now, default))!.RetainedReportCount);
    }

    [IntegrationFact]
    public async Task Should_use_constant_query_counts_and_content_free_list_projections()
    {
        for (int i = 0; i < 5; i++)
        {
            CrashGroup group = Group();
            await SeedAsync(group, Report(group, s_now), Report(group, s_now.AddDays(-1)));
        }
        var probe = new QueryProbe();
        CrashReadStore read = await ProbedReadAsync(probe);
        CrashGroupPage single = await read.ListGroupsAsync(new(PageSize: 1), s_now, default);
        int smallPageQueries = probe.Queries.Count;
        Assert.InRange(smallPageQueries, 1, 5);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(single));
        probe.Queries.Clear();
        await read.ListGroupsAsync(new(PageSize: 100), s_now, default);
        Assert.Equal(smallPageQueries, probe.Queries.Count);
        Assert.All(probe.Queries, x =>
        {
            Assert.DoesNotContain("stack", x.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("notes", x.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("meta", x.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("object_key", x.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("log_tail", x.Sql, StringComparison.OrdinalIgnoreCase);
        });
        probe.Queries.Clear();
        await read.GetGroupAsync(single.Items[0].Id, 1, 1, s_now, default);
        int smallReportQueries = probe.Queries.Count;
        // A constant count however many reports: eleven since the client's GPU breakdown joined the driver's (#1252).
        Assert.InRange(smallReportQueries, 1, 11);
        probe.Queries.Clear();
        await read.GetGroupAsync(single.Items[0].Id, 1, 100, s_now, default);
        Assert.Equal(smallReportQueries, probe.Queries.Count);
        Assert.Single(probe.Queries, x => x.Sql.Contains(".stack", StringComparison.OrdinalIgnoreCase));
        Assert.All(probe.Queries, x => Assert.Equal(5, x.Timeout));
    }

    [IntegrationFact]
    public async Task Should_cancel_blocked_queries_and_enforce_five_second_database_timeout()
    {
        await using CrashesDbContext blocker = _postgres.Factory.CreateDbContext();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlRawAsync("LOCK TABLE crash_groups IN ACCESS EXCLUSIVE MODE");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read.ListGroupsAsync(new(), s_now, cancel.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4));
        watch.Restart();
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Read.ListGroupsAsync(new(), s_now, default));
        Assert.IsType<TimeoutException>(failure.GetBaseException());
        Assert.InRange(watch.Elapsed.TotalSeconds, 4, 10);
    }

    [IntegrationFact]
    public async Task Should_apply_earlier_lifecycle_expiry_before_cleanup()
    {
        CrashGroup group = Group();
        CrashReport newestButExpired = Report(group, s_now.AddSeconds(-1));
        newestButExpired.ExpiresAt = s_now;
        CrashReport retained = Report(group, s_now.AddSeconds(-2));
        await SeedAsync(group, newestButExpired, retained);
        CrashGroupDetail detail = (await Read.GetGroupAsync(group.Id, 1, 25, s_now, default))!;
        Assert.Equal(1, detail.RetainedReportCount);
        Assert.Equal(retained.Id, detail.LatestReportId);
        Assert.Null(await Read.GetReportAsync(newestButExpired.Id, s_now, default));
        Assert.Equal(1, Assert.Single((await Read.ListGroupsAsync(new(), s_now, default)).Items).MatchingReportCount);
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        Assert.Equal(2, await db.Reports.CountAsync());
    }
    private async Task<CrashReadStore> ProbedReadAsync(QueryProbe probe)
    {
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        return new CrashReadStore(new ProbedContextFactory(db.Database.GetConnectionString()!, probe));
    }
    private async Task SeedAsync(CrashGroup group, params CrashReport[] reports)
    {
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        db.Groups.Add(group);
        db.Reports.AddRange(reports);
        await db.SaveChangesAsync();
    }

    private static CrashGroup Group() => new()
    {
        SignatureVersion = 1,
        SignatureHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        CanonicalSignature = "private canonical",
        Signature = "runtime.exe!Fault",
        FirstSeen = s_now.AddDays(-100),
        LastSeen = s_now,
        FirstVersion = "1.0.0",
        LastVersion = "2.0.0",
        ReportCount = 2,
        Notes = "private note"
    };

    private static CrashReport Report(CrashGroup group, DateTimeOffset receivedAt, string version = "2.0.0") => new()
    {
        ObjectKey = "private-key/" + Guid.NewGuid().ToString("N"),
        SourceETag = "private-etag",
        SourceSize = 1,
        ReceivedAt = receivedAt,
        ExpiresAt = receivedAt.AddDays(90),
        Version = version,
        GitHash = "private-hash",
        Status = CrashReportStatus.Processed,
        GroupId = group.Id,
        ProcessedAt = s_now,
        SignatureVersion = 1,
        ExceptionCode = 0xc0000005,
        ExceptionAddress = "0x0000000000000010",
        Cpu = "private cpu",
        Stack = JsonSerializer.Serialize(Analysis()),
        StackSchemaVersion = 1,
        QualityFlags = CrashQualityFlags.ScanFrames,
        LogTail = "private log"
    };

    private static CrashAnalysis Analysis() => new(1,
        new CrashIdentity("EXCEPTION_ACCESS_VIOLATION", 0xc0000005, "0x0000000000000010", 7, 42),
        new CrashSystem("Windows NT", "10", null, "x86_64", null),
        [new CrashThread(7, 42, [new CrashFrame("runtime.exe", "0x0000000000000010", "Fault", null, null, [], "scan")]),
            new CrashThread(0, 1, [new CrashFrame("runtime.exe", null, "Other", null, null, [], "context")])],
        [new CrashModule("nvwgf2umx.dll", "IMAGE1", null, null, "42", null, null, false),
            new CrashModule("atidxx64.dll", "IMAGE2", null, null, "43", null, null, false)],
        new CrashMetadata(1, "runtime.exe", "2.0.0", "private metadata", 0xc0000005, "0x0000000000000010", 42, s_now),
        null, CrashQualityFlags.ScanFrames, new ProcessorIdentity("0.27.0", "2.3.9", "v1"));
}
