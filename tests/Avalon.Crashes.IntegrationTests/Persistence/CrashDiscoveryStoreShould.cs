using Avalon.Crashes.Persistence;
using Avalon.Database.Crashes;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Persistence;

[Trait("Category", "CrashesPostgres")]
public sealed class CrashDiscoveryStoreShould : IAsyncLifetime
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgresFixture _postgres = new();
    public Task InitializeAsync() => _postgres.InitializeAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync();

    [IntegrationFact]
    public async Task Reject_malformed_keys_durably_without_reconsidering_terminal_rows()
    {
        var source = new StoredCrashObject("crashes/1.2.3/2026-10-23/arbitrary.zip", "etag", 10, s_now);
        Assert.True(await _postgres.Queue.DiscoverAsync(source, s_now, default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        CrashReport report = await db.Reports.AsNoTracking().SingleAsync();
        Assert.Equal(CrashReportStatus.Rejected, report.Status);
        Assert.Equal(CrashFailureCode.InvalidObjectKey, report.ReasonCode);
        Assert.Null(await _postgres.Queue.ClaimAsync(s_now, default));
        Assert.False(await _postgres.Queue.DiscoverAsync(source with { ETag = "replacement" }, s_now, default));
        report = await db.Reports.AsNoTracking().SingleAsync();
        Assert.Equal(source.Key, report.ObjectKey);
        Assert.Equal(source.ETag, report.SourceETag);
        Assert.Null(report.Stack);
    }

    [IntegrationFact]
    public async Task Reject_whitespace_and_other_nonconforming_s3_keys_without_renaming_them()
    {
        string[] keys = [" ", "orphan.zip", "1.2.3/2026-02-29/11111111111111111111111111111111.zip", "1.2.3/2026-10-23/not-a-guid.zip"];
        foreach (string key in keys)
            Assert.True(await _postgres.Queue.DiscoverAsync(new StoredCrashObject(key, "etag", 10, s_now), s_now, default));
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        CrashReport[] reports = await db.Reports.ToArrayAsync();
        Assert.Equal(keys.Length, reports.Length);
        Assert.All(reports, x =>
        {
            Assert.Contains(x.ObjectKey, keys);
            Assert.Equal(CrashReportStatus.Rejected, x.Status);
            Assert.Equal(CrashFailureCode.InvalidObjectKey, x.ReasonCode);
        });
    }

    [IntegrationFact]
    public async Task Roundtrip_opaque_page_and_per_version_date_progress()
    {
        var store = new CrashDiscoveryStore(_postgres.Factory);
        DiscoveryCursor cursor = await store.LoadAsync(default);
        cursor.LastScanAt = s_now;
        cursor.ContinuationToken = "root/opaque==";
        cursor.BackfillContinuationToken = "inventory+opaque==";
        cursor.DiscoveredVersions = ["1.2.3"];
        await store.SaveAsync(cursor, default);
        cursor = await store.LoadAsync(default);
        DiscoveryVersion version = Assert.Single(cursor.Versions);
        version.RecentDate = new DateOnly(2026, 10, 23);
        version.TodayToken = "today";
        version.YesterdayToken = "yesterday";
        version.DatePrefixToken = "dates";
        version.DateInventoryReady = true;
        version.HistoricalDate = new DateOnly(2026, 10, 20);
        version.HistoricalToken = "historical";
        version.Phase = 3;
        version.LastVisitedAt = s_now;
        cursor.UpdatedVersions = [version];
        cursor.DiscoveredDates = [new DiscoveryDatePrefix(version.Version, version.HistoricalDate.Value)];
        await store.SaveAsync(cursor, default);
        cursor = await store.LoadAsync(default);
        Assert.Equal("root/opaque==", cursor.ContinuationToken);
        Assert.Equal("inventory+opaque==", cursor.BackfillContinuationToken);
        version = Assert.Single(cursor.Versions);
        Assert.Equal("today", version.TodayToken);
        Assert.Equal("yesterday", version.YesterdayToken);
        Assert.Equal("dates", version.DatePrefixToken);
        Assert.Equal("historical", version.HistoricalToken);
        Assert.True(version.DateInventoryReady);
        Assert.Equal(3, version.Phase);
        Assert.Equal(new DateOnly(2026, 10, 20), Assert.Single(version.Dates).Date);
        cursor.CompletedDates = [new DiscoveryDatePrefix(version.Version, version.HistoricalDate!.Value)];
        await store.SaveAsync(cursor, default);
        Assert.Equal(s_now, Assert.Single((await store.LoadAsync(default)).Versions.Single().Dates).LastScannedAt);
    }

    [IntegrationFact]
    public async Task Bound_loads_and_prioritize_unvisited_versions_after_round_robin_progress()
    {
        var store = new CrashDiscoveryStore(_postgres.Factory);
        DiscoveryCursor cursor = await store.LoadAsync(default);
        cursor.LastScanAt = s_now;
        cursor.DiscoveredVersions = Enumerable.Range(0, 100).Select(x => "0.1." + x).ToArray();
        await store.SaveAsync(cursor, default);
        cursor.DiscoveredVersions = Enumerable.Range(100, 100).Select(x => "0.1." + x).ToArray();
        await store.SaveAsync(cursor, default);
        cursor = await store.LoadAsync(default);
        Assert.Equal(100, cursor.Versions.Count);
        string[] first = cursor.Versions.Select(x => x.Version).ToArray();
        foreach (DiscoveryVersion version in cursor.Versions)
            version.LastVisitedAt = s_now;
        cursor.UpdatedVersions = cursor.Versions;
        await store.SaveAsync(cursor, default);
        cursor = await store.LoadAsync(default);
        Assert.Equal(100, cursor.Versions.Count);
        Assert.DoesNotContain(cursor.Versions, x => first.Contains(x.Version, StringComparer.Ordinal));
    }

    [IntegrationFact]
    public async Task Retire_and_recreate_inactive_scheduler_state_without_deleting_reports()
    {
        var store = new CrashDiscoveryStore(_postgres.Factory);
        var cursor = new DiscoveryCursor
        {
            LastScanAt = s_now,
            DiscoveredVersions = ["1.2.3"],
            DiscoveredDates = [new DiscoveryDatePrefix("1.2.3", new DateOnly(2026, 10, 20))]
        };
        await store.SaveAsync(cursor, default);
        var source = new StoredCrashObject("1.2.3/2026-10-23/11111111111111111111111111111111.zip", "etag", 10, s_now);
        await _postgres.Queue.DiscoverAsync(source, s_now, default);
        cursor.DiscoveredVersions = [];
        cursor.DiscoveredDates = [];
        cursor.LastScanAt = s_now.AddDays(92);
        await store.SaveAsync(cursor, default);
        Assert.Empty((await store.LoadAsync(default)).Versions);
        await using CrashesDbContext db = _postgres.Factory.CreateDbContext();
        Assert.Single(await db.Reports.ToArrayAsync());
        Assert.Empty(await db.DiscoveryDates.ToArrayAsync());
        cursor.DiscoveredVersions = ["1.2.3"];
        await store.SaveAsync(cursor, default);
        Assert.Equal("1.2.3", Assert.Single((await store.LoadAsync(default)).Versions).Version);
    }

    [IntegrationFact]
    public async Task Preserve_discovered_versions_when_overlapping_cursor_snapshots_are_saved()
    {
        var store = new CrashDiscoveryStore(_postgres.Factory);
        DiscoveryCursor first = await store.LoadAsync(default);
        DiscoveryCursor second = await store.LoadAsync(default);
        first.LastScanAt = s_now;
        first.DiscoveredVersions = ["1.2.3"];
        second.LastScanAt = s_now.AddMinutes(1);
        second.DiscoveredVersions = ["2.3.4"];
        await Task.WhenAll(store.SaveAsync(first, default), store.SaveAsync(second, default));
        DiscoveryCursor saved = await store.LoadAsync(default);
        Assert.Equal(s_now.AddMinutes(1), saved.LastScanAt);
        Assert.Equal(2, saved.Versions.Count);
    }

    [IntegrationFact]
    public async Task Refuse_unbounded_or_malformed_version_inventory_changes()
    {
        var store = new CrashDiscoveryStore(_postgres.Factory);
        var cursor = new DiscoveryCursor { LastScanAt = s_now, DiscoveredVersions = ["../"] };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(cursor, default));
        cursor.DiscoveredVersions = Enumerable.Range(0, 101).Select(x => "0.1." + x).ToArray();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(cursor, default));
        Assert.Empty((await store.LoadAsync(default)).Versions);
    }
}
