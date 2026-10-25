using Avalon.Crashes;
using Avalon.Crashes.Persistence;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Database.Crashes;

public sealed class CrashMaintenanceStore(IDbContextFactory<CrashesDbContext> factory, TimeProvider? timeProvider = null) : ICrashMaintenanceStore
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<CrashQueueSnapshot> SnapshotAsync(DateTimeOffset now, CancellationToken ct)
    {
        long started = _time.GetTimestamp();
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        IQueryable<CrashReport> pending = db.Reports.Where(x => x.ExpiresAt > now &&
            (x.Status == CrashReportStatus.Pending || x.Status == CrashReportStatus.RetryWait || x.Status == CrashReportStatus.Processing));
        long count = await pending.LongCountAsync(ct);
        DateTimeOffset? oldest = await pending.MinAsync(x => (DateTimeOffset?)x.ReceivedAt, ct);
        DateTimeOffset? sweep = await db.DiscoveryCursors.Select(x => x.LastBackfillAt).SingleOrDefaultAsync(ct);
        DateTimeOffset cutoff = now + _time.GetElapsedTime(started);
        long expired = await db.Reports.LongCountAsync(x => x.ExpiresAt <= cutoff, ct);
        return new(count, oldest, sweep, expired);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct)
    {
        if (batchSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        long started = _time.GetTimestamp();
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        now = now.ToUniversalTime() + _time.GetElapsedTime(started);
        // A single statement is one transaction. Do not skip locked rows: zero must mean fully purged.
        return await db.Database.ExecuteSqlAsync($"""
            DELETE FROM crash_reports WHERE id IN
              (SELECT id FROM crash_reports WHERE expires_at <= {now.ToUniversalTime()}
               ORDER BY expires_at, id LIMIT {batchSize} FOR UPDATE)
            """, ct);
    }

    public async Task<FailurePage> ListFailuresAsync(int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(page));
        DateTimeOffset now = _time.GetUtcNow();
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        // Only finite failure codes and server identity/times. Never project keys or reason_detail.
        CrashFailure[] rows = await db.Reports.AsNoTracking()
            .Where(x => x.ExpiresAt > now && (x.Status == CrashReportStatus.Failed || x.Status == CrashReportStatus.Rejected))
            .OrderByDescending(x => x.ReceivedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new CrashFailure(x.Id.Value, x.Status, x.ReasonCode, x.ReceivedAt, x.ExpiresAt, x.AttemptCount))
            .ToArrayAsync(ct);
        DateTimeOffset current = _time.GetUtcNow();
        return new FailurePage(page, pageSize, rows.Where(x => x.ExpiresAt > current).ToArray());
    }

    public async Task<RetryResult> RetryFailedAsync(CrashReportId id, DateTimeOffset now, CancellationToken ct)
    {
        long started = _time.GetTimestamp();
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        CrashReport? row = await db.Reports.FromSql($"SELECT * FROM crash_reports WHERE id = {id.Value} FOR UPDATE").SingleOrDefaultAsync(ct);
        now = now.ToUniversalTime() + _time.GetElapsedTime(started);
        if (row is null || row.Status != CrashReportStatus.Failed || row.ExpiresAt <= now)
            return RetryResult.NotEligible;
        long acquired = _time.GetTimestamp();
        int count = await db.Database.ExecuteSqlAsync($"""
            UPDATE crash_reports SET status = 'pending', reason_code = NULL, reason_detail = NULL,
              retry_campaign_started_at = {now}, retry_campaign_attempt_count = 0,
              consecutive_tool_failures = 0, last_manual_retry_at = {now}, next_attempt_at = NULL,
              lease_owner = NULL, lease_until = NULL, completion_token = NULL
            WHERE id = {id.Value} AND status = 'failed' AND expires_at > {now}
            """, ct);
        if (row.ExpiresAt <= now + _time.GetElapsedTime(acquired))
            return RetryResult.NotEligible;
        await transaction.CommitAsync(ct);
        return count == 1 ? RetryResult.Retried : RetryResult.NotEligible;
    }
}
