using System.Data;
using System.Linq.Expressions;
using System.Text.Json;
using Avalon.Crashes;
using Avalon.Crashes.Queries;
using Avalon.Crashes.Validation;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Database.Crashes;

public sealed class CrashReadStore(IDbContextFactory<CrashesDbContext> factory) : ICrashReadStore
{
    private const int RetentionDays = 90;
    private static readonly Expression<Func<CrashGroup, CrashGroupSummary>> s_summary = x => new(
        x.Id, x.Signature, x.SignatureVersion, x.Status, x.FixedInVersion, x.IssueUrl, x.IsRegression,
        x.RegressionCount, x.LastRegressionAt, x.TriageRevision, x.TriagedAt, x.FixedAt,
        x.FirstSeen, x.LastSeen, x.FirstVersion, x.LastVersion, x.ReportCount, 0, 0, Array.Empty<CrashDailyCount>());

    public async Task<CrashGroupPage> ListGroupsAsync(CrashGroupFilter filter, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidatePage(filter.Page, filter.PageSize);
        now = now.ToUniversalTime();
        if (filter.Status is { } status && !Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(filter));
        if (filter.Version is { } version && !CrashObjectKey.IsVersion(version))
            throw new ArgumentException("Invalid exact report version.", nameof(filter));
        if (filter.Since > now)
            throw new ArgumentOutOfRangeException(nameof(filter));
        DateTimeOffset effectiveSince = EffectiveSince(now, filter.Since);
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(5);
        await using IDbContextTransaction snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        IQueryable<CrashReport> retained = Retained(db, now);
        IQueryable<CrashReport> matching = Match(retained, filter.Version, effectiveSince);
        IQueryable<CrashGroup> groups = db.Groups.AsNoTracking();
        if (filter.Status is { } exactStatus)
            groups = groups.Where(x => x.Status == exactStatus);
        if (filter.Version is not null || filter.Since is not null)
            groups = groups.Where(x => matching.Any(r => r.GroupId == x.Id));
        long total = await groups.LongCountAsync(ct);
        CrashGroupSummary[] rows = await groups
            .OrderBy(x => x.IsRegression ? 0 : x.Status == CrashGroupStatus.New ? 1 : 2)
            .ThenByDescending(x => x.LastSeen).ThenBy(x => x.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(s_summary).ToArrayAsync(ct);
        rows = await WithCountsAsync(rows, retained, matching, now, ct);
        await snapshot.CommitAsync(ct);
        return new(rows, filter.Page, filter.PageSize, total, now, RetentionDays, effectiveSince);
    }

    public async Task<CrashGroupDetail?> GetGroupAsync(CrashGroupId id, int reportsPage, int reportsPageSize, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(id);
        ValidatePage(reportsPage, reportsPageSize);
        now = now.ToUniversalTime();
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(5);
        await using IDbContextTransaction snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        // Separate scalar projection avoids retrieving the canonical signature or report identity tie-break state.
        CrashGroupSummary? summary = await db.Groups.AsNoTracking().Where(x => x.Id == id).Select(s_summary).SingleOrDefaultAsync(ct);
        if (summary is null)
            return null;
        string? notes = await db.Groups.Where(x => x.Id == id).Select(x => x.Notes).SingleAsync(ct);
        IQueryable<CrashReport> retained = Retained(db, now).Where(x => x.GroupId == id);
        CrashGroupSummary[] counted = await WithCountsAsync([summary], retained, retained, now, ct);
        summary = counted[0];
        CrashVersionCount[] versions = await retained.GroupBy(x => x.Version ?? "unknown")
            .OrderBy(x => x.Key).Select(x => new CrashVersionCount(x.Key, x.LongCount())).ToArrayAsync(ct);
        CrashGpuDriverCount[] drivers = await retained
            .GroupBy(x => new { Module = x.GpuDriverModule ?? "unknown", Version = x.GpuDriverVersion ?? "unknown", Source = x.GpuDriverSource ?? "unknown" })
            .OrderBy(x => x.Key.Module).ThenBy(x => x.Key.Version).ThenBy(x => x.Key.Source)
            .Select(x => new CrashGpuDriverCount(x.Key.Module, x.Key.Version, x.Key.Source, x.LongCount())).ToArrayAsync(ct);
        CrashOsBuildCount[] os = await retained.GroupBy(x => x.OsBuild ?? "unknown")
            .OrderBy(x => x.Key).Select(x => new CrashOsBuildCount(x.Key, x.LongCount())).ToArrayAsync(ct);
        CrashReportSummary[] reports = await retained.OrderByDescending(x => x.ReceivedAt).ThenBy(x => x.Id)
            .Skip((reportsPage - 1) * reportsPageSize).Take(reportsPageSize)
            .Select(x => new CrashReportSummary(x.Id, x.ReceivedAt, x.ExpiresAt, x.Version,
                x.ExceptionCode == null ? null : (uint?)x.ExceptionCode, x.QualityFlags, x.CrashingModule)).ToArrayAsync(ct);
        // Latest stack selection is deliberately independent of reportsPage and its projection excludes log/meta.
        var latest = await retained.OrderByDescending(x => x.ReceivedAt).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.ReceivedAt, x.Stack, x.QualityFlags }).FirstOrDefaultAsync(ct);
        CrashAnalysis? analysis = latest?.Stack is { } stack ? JsonSerializer.Deserialize<CrashAnalysis>(stack) : null;
        // The projector puts the crashing thread first but preserves its upstream Index, which is not a list offset.
        CrashThread? crashingThread = analysis?.Threads.FirstOrDefault(x => x.Index == analysis.Crash.CrashingThreadIndex);
        await snapshot.CommitAsync(ct);
        return new(summary, notes, now, now.AddDays(-RetentionDays), now, summary.RetainedReportCount,
            latest?.Id, latest?.ReceivedAt, crashingThread, latest?.QualityFlags,
            versions, drivers, os, new(reports, reportsPage, reportsPageSize, summary.RetainedReportCount));
    }

    public async Task<CrashReportDetail?> GetReportAsync(CrashReportId id, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(id);
        now = now.ToUniversalTime();
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(5);
        // One statement is a consistent snapshot. Never select source keys, ETags or arbitrary reason details.
        var row = await db.Reports.AsNoTracking().Where(x => x.Id == id && x.ExpiresAt > now)
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.ReasonCode,
                x.ReceivedAt,
                x.ExpiresAt,
                x.ProcessedAt,
                x.GroupId,
                x.Version,
                x.GitHash,
                x.ClientCrashedAt,
                x.ExceptionCode,
                x.ExceptionAddress,
                x.CrashingModule,
                x.OsVersion,
                x.OsBuild,
                x.Cpu,
                x.GpuDriverModule,
                x.GpuDriverVersion,
                x.GpuDriverSource,
                x.QualityFlags,
                x.Stack,
                x.LogTail
            }).SingleOrDefaultAsync(ct);
        if (row is null)
            return null;
        if (row.Status != CrashReportStatus.Processed)
        {
            return new(row.Id, row.Status, row.ReasonCode, row.ReceivedAt, row.ExpiresAt,
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, CrashQualityFlags.None, null, null);
        }

        CrashAnalysis? analysis = row.Stack is { } stack ? JsonSerializer.Deserialize<CrashAnalysis>(stack) : null;
        return new(row.Id, row.Status, row.ReasonCode, row.ReceivedAt, row.ExpiresAt,
            row.ProcessedAt, row.GroupId, row.Version, row.GitHash, row.ClientCrashedAt,
            row.ExceptionCode is { } code ? (uint)code : null, row.ExceptionAddress, row.CrashingModule,
            row.OsVersion, row.OsBuild, row.Cpu, row.GpuDriverModule, row.GpuDriverVersion, row.GpuDriverSource,
            row.QualityFlags, analysis, row.LogTail);
    }

    private static IQueryable<CrashReport> Retained(CrashesDbContext db, DateTimeOffset now) => db.Reports.AsNoTracking()
        .Where(x => x.Status == CrashReportStatus.Processed && x.ExpiresAt > now);

    private static IQueryable<CrashReport> Match(IQueryable<CrashReport> retained, string? version, DateTimeOffset since)
    {
        IQueryable<CrashReport> matching = retained.Where(x => x.ReceivedAt >= since);
        return version is null ? matching : matching.Where(x => x.Version == version);
    }

    private static async Task<CrashGroupSummary[]> WithCountsAsync(CrashGroupSummary[] groups,
        IQueryable<CrashReport> retained, IQueryable<CrashReport> matching, DateTimeOffset now, CancellationToken ct)
    {
        if (groups.Length == 0)
            return groups;
        CrashGroupId[] ids = groups.Select(x => x.Id).ToArray();
        // Aggregate in SQL for the complete selected page, never a follow-up query per group/report.
        Dictionary<CrashGroupId, long> totals = await retained.Where(x => ids.Contains(x.GroupId!)).GroupBy(x => x.GroupId)
            .Select(x => new { Id = x.Key!, Count = x.LongCount() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        Dictionary<CrashGroupId, long> matched = await matching.Where(x => ids.Contains(x.GroupId!)).GroupBy(x => x.GroupId)
            .Select(x => new { Id = x.Key!, Count = x.LongCount() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        DateTimeOffset firstDay = new(now.UtcDateTime.Date.AddDays(-29), TimeSpan.Zero);
        var daily = await matching.Where(x => ids.Contains(x.GroupId!) && x.ReceivedAt >= firstDay && x.ReceivedAt < firstDay.AddDays(30))
            .GroupBy(x => new { x.GroupId, Date = DateOnly.FromDateTime(x.ReceivedAt.UtcDateTime) })
            .Select(x => new { x.Key.GroupId, x.Key.Date, Count = x.LongCount() }).ToArrayAsync(ct);
        var dailyByGroup = daily.ToLookup(x => x.GroupId!);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return groups.Select(group =>
        {
            var dates = dailyByGroup[group.Id].ToDictionary(x => x.Date, x => x.Count);
            CrashDailyCount[] counts = Enumerable.Range(-29, 30).Select(offset =>
            {
                DateOnly date = today.AddDays(offset);
                return new CrashDailyCount(date, dates.GetValueOrDefault(date));
            }).ToArray();
            return group with { RetainedReportCount = totals.GetValueOrDefault(group.Id), MatchingReportCount = matched.GetValueOrDefault(group.Id), DailyCounts = counts };
        }).ToArray();
    }

    private static DateTimeOffset EffectiveSince(DateTimeOffset now, DateTimeOffset? since)
        => since is { } value && value > now.AddDays(-RetentionDays) ? value.ToUniversalTime() : now.AddDays(-RetentionDays);

    private static void ValidatePage(int page, int pageSize)
    {
        if (page is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
    }
}
