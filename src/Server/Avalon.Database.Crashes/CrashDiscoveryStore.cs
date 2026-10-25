using Avalon.Crashes.Persistence;
using Avalon.Crashes.Validation;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Database.Crashes;

public sealed class CrashDiscoveryStore(IDbContextFactory<CrashesDbContext> factory) : ICrashDiscoveryStore
{
    public async Task<DiscoveryCursor> LoadAsync(CancellationToken ct)
    {
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        CrashDiscoveryCursor saved = await db.DiscoveryCursors.AsNoTracking().SingleAsync(ct);
        // PostgreSQL sorts NULL last by default. Unvisited versions must come first.
        DiscoveryVersion[] versions = await db.DiscoveryVersions.AsNoTracking()
            .OrderBy(x => x.LastVisitedAt != null).ThenBy(x => x.LastVisitedAt).ThenBy(x => x.Version)
            .Take(DiscoveryCursor.VersionBatchSize)
            .Select(x => new DiscoveryVersion
            {
                Version = x.Version,
                LastVisitedAt = x.LastVisitedAt,
                RecentDate = x.RecentDate,
                TodayToken = x.TodayToken,
                YesterdayToken = x.YesterdayToken,
                DatePrefixToken = x.DatePrefixToken,
                Phase = x.Phase,
                DateInventoryReady = x.DateInventoryReady,
                HistoricalDate = x.HistoricalDate,
                HistoricalToken = x.HistoricalToken
            }).ToArrayAsync(ct);
        if (saved.LastScanAt is { } scan)
        {
            var today = DateOnly.FromDateTime(scan.UtcDateTime);
            string[] selected = versions.Select(x => x.Version).ToArray();
            CrashDiscoveryDate[] dates = await db.DiscoveryDates.AsNoTracking()
                .Where(x => selected.Contains(x.Version) && x.Date >= today.AddDays(-91) && x.Date <= today.AddDays(-2))
                .ToArrayAsync(ct);
            foreach (DiscoveryVersion version in versions)
                version.Dates = dates.Where(x => string.Equals(x.Version, version.Version, StringComparison.Ordinal)).Select(x => new DiscoveryDate(x.Date, x.LastScannedAt)).ToArray();
        }
        return new DiscoveryCursor
        {
            ContinuationToken = saved.ContinuationToken,
            BackfillContinuationToken = saved.BackfillContinuationToken,
            LastScanAt = saved.LastScanAt,
            LastBackfillAt = saved.LastBackfillAt,
            Versions = versions
        };
    }

    public async Task SaveAsync(DiscoveryCursor cursor, CancellationToken ct)
    {
        if (cursor.DiscoveredVersions.Count > DiscoveryCursor.VersionBatchSize || cursor.UpdatedVersions.Count > DiscoveryCursor.VersionBatchSize
            || cursor.DiscoveredDates.Count > 100 || cursor.CompletedDates.Count > 1
            || cursor.DiscoveredVersions.Any(x => !CrashObjectKey.IsVersion(x))
            || cursor.UpdatedVersions.Any(x => !CrashObjectKey.IsVersion(x.Version) || x.Phase is < 0 or > 3))
        {
            throw new ArgumentException("Invalid discovery state.", nameof(cursor));
        }
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        CrashDiscoveryCursor saved = await db.DiscoveryCursors.FromSqlRaw("SELECT * FROM crash_discovery_cursor WHERE id = 1 FOR UPDATE").SingleAsync(ct);
        saved.ContinuationToken = cursor.ContinuationToken;
        saved.BackfillContinuationToken = cursor.BackfillContinuationToken;
        saved.LastScanAt = Later(saved.LastScanAt, cursor.LastScanAt);
        saved.LastBackfillAt = Later(saved.LastBackfillAt, cursor.LastBackfillAt);
        DateTimeOffset seen = cursor.LastScanAt ?? throw new ArgumentException("Discovery scan time is required.", nameof(cursor));
        foreach (string version in cursor.DiscoveredVersions.Distinct(StringComparer.Ordinal))
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO crash_discovery_versions (version, last_seen_at, phase, date_inventory_ready) VALUES ({version}, {seen}, 0, false)
                ON CONFLICT (version) DO UPDATE SET last_seen_at = GREATEST(crash_discovery_versions.last_seen_at, EXCLUDED.last_seen_at)
                """, ct);
        }
        foreach (DiscoveryVersion version in cursor.UpdatedVersions)
        {
            await db.Database.ExecuteSqlAsync($"""
                UPDATE crash_discovery_versions SET last_visited_at = {version.LastVisitedAt}, recent_date = {version.RecentDate},
                    today_token = {version.TodayToken}, yesterday_token = {version.YesterdayToken},
                    date_prefix_token = {version.DatePrefixToken}, phase = {version.Phase}, date_inventory_ready = {version.DateInventoryReady},
                    historical_date = {version.HistoricalDate}, historical_token = {version.HistoricalToken}
                WHERE version = {version.Version}
                """, ct);
        }
        var currentDate = DateOnly.FromDateTime(seen.UtcDateTime);
        foreach (DiscoveryDatePrefix date in cursor.DiscoveredDates)
        {
            if (!CrashObjectKey.IsVersion(date.Version) || date.Date < currentDate.AddDays(-91) || date.Date > currentDate.AddDays(-2))
                throw new ArgumentException("Invalid discovery date.", nameof(cursor));
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO crash_discovery_dates (version, date) VALUES ({date.Version}, {date.Date})
                ON CONFLICT (version, date) DO NOTHING
                """, ct);
        }
        foreach (DiscoveryDatePrefix date in cursor.CompletedDates)
        {
            await db.Database.ExecuteSqlAsync($"""
                UPDATE crash_discovery_dates SET last_scanned_at = {seen} WHERE version = {date.Version} AND date = {date.Date}
                """, ct);
        }
        await db.DiscoveryDates.Where(x => x.Date < currentDate.AddDays(-91) || x.Date > currentDate.AddDays(-2)).ExecuteDeleteAsync(ct);
        // Retire only scheduler state. Root refresh can recreate it, and full inventory
        // remains independent, so late uploads and report history are never deleted.
        DateTimeOffset cutoff = saved.LastScanAt!.Value.AddDays(-91);
        await db.DiscoveryVersions.Where(x => x.LastSeenAt < cutoff).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static DateTimeOffset? Later(DateTimeOffset? left, DateTimeOffset? right) => left is null ? right : right is null || left > right ? left : right;
}
