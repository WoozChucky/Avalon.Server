using System.Text;
using System.Text.Json;
using Avalon.Crashes;
using Avalon.Crashes.Analysis;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Triage;
using Avalon.Crashes.Validation;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Database.Crashes;

public sealed class CrashQueue(IDbContextFactory<CrashesDbContext> factory, TimeProvider? timeProvider = null,
    ICrashQueueObserver? observer = null) : ICrashQueue
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private static readonly TimeSpan s_leaseDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan s_retryHorizon = TimeSpan.FromHours(24);

    public async Task<bool> DiscoverAsync(StoredCrashObject source, DateTimeOffset now, CancellationToken ct)
    {
        long startedAt = _timeProvider.GetTimestamp();
        ArgumentException.ThrowIfNullOrEmpty(source.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.ETag);
        if (Encoding.UTF8.GetByteCount(source.Key) > 1024 || source.ETag.Length > 1024 || source.Size < 0)
            throw new ArgumentException("Invalid crash object fingerprint.", nameof(source));

        now = now.ToUniversalTime();
        DateTimeOffset receivedAt = source.LastModified.ToUniversalTime();
        DateTimeOffset expiresAt = receivedAt.AddDays(90);
        if (expiresAt <= CurrentTime(now, startedAt))
            return false;

        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        if (expiresAt <= CurrentTime(now, startedAt))
            return false;

        var id = Guid.NewGuid();
        string? reason = !CrashObjectKey.TryParse(source.Key, out _) ? "invalid_object_key"
            : source.Size > CrashLimits.ArchiveBytes ? "archive_too_large" : null;
        string status = reason is null ? "pending" : "rejected";
        bool firstRejection = reason is not null;
        int inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO crash_reports (id, object_key, source_etag, source_size, received_at, expires_at, status, reason_code, attempt_count, consecutive_tool_failures, quality_flags)
            VALUES ({id}, {source.Key}, {source.ETag}, {source.Size}, {receivedAt}, {expiresAt}, {status}, {reason}, 0, 0, 'none')
            ON CONFLICT (object_key) DO NOTHING
            """, ct);
        if (inserted == 0)
        {
            firstRejection = false;
            CrashReport existing = await db.Reports.FromSql($"SELECT * FROM crash_reports WHERE object_key = {source.Key} FOR UPDATE").SingleAsync(ct);
            if (existing.ExpiresAt <= CurrentTime(now, startedAt))
                return false;

            if (IsActive(existing.Status) && !MatchesSource(existing, source))
            {
                FinishWithoutInput(existing, CrashReportStatus.Rejected, CrashFailureCode.ObjectChanged, null);
                firstRejection = existing.LastManualRetryAt is null;
                await db.SaveChangesAsync(ct);
            }
        }
        if (expiresAt <= CurrentTime(now, startedAt))
            return false;

        await transaction.CommitAsync(ct);
        if (firstRejection)
            Observe(() => observer?.Terminal(VersionOf(source.Key), CrashReportStatus.Rejected));
        return inserted == 1 && expiresAt > CurrentTime(now, startedAt);
    }

    public async Task<CrashLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct)
    {
        long startedAt = _timeProvider.GetTimestamp();
        now = now.ToUniversalTime();
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        while (true)
        {
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            DateTimeOffset current = CurrentTime(now, startedAt);
            CrashReport? report = await db.Reports.FromSql($"""
                SELECT * FROM crash_reports
                WHERE expires_at > {current} AND
                    (status = 'pending' OR (status = 'retry_wait' AND next_attempt_at <= {current}) OR (status = 'processing' AND lease_until <= {current}))
                ORDER BY COALESCE(next_attempt_at, received_at), id
                LIMIT 1 FOR UPDATE SKIP LOCKED
                """).SingleOrDefaultAsync(ct);
            if (report is null)
                return null;

            current = CurrentTime(now, startedAt);
            if (report.ExpiresAt <= current)
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                continue;
            }

            bool recovered = report.Status == CrashReportStatus.Processing;
            if (recovered)
            {
                // An abandoned in-flight attempt is a resource failure, even if its process never returned a result.
                report.ConsecutiveToolFailures++;
                report.ReasonCode = CrashFailureCode.ResourceLimit;
            }
            if (report.ConsecutiveToolFailures >= 3 || report.RetryCampaignStartedAt + s_retryHorizon <= current)
            {
                FinishWithoutInput(report, CrashReportStatus.Failed, report.ReasonCode ?? CrashFailureCode.ResourceLimit, null);
                await db.SaveChangesAsync(ct);
                if (report.ExpiresAt <= CurrentTime(now, startedAt))
                {
                    await transaction.RollbackAsync(ct);
                }
                else
                {
                    await transaction.CommitAsync(ct);
                    if (report.LastManualRetryAt is null)
                        Observe(() => observer?.Terminal(VersionOf(report.ObjectKey), CrashReportStatus.Failed));
                    if (recovered)
                        Observe(() => observer?.LeaseRecovered());
                }
                db.ChangeTracker.Clear();
                continue;
            }

            report.Status = CrashReportStatus.Processing;
            report.AttemptCount++;
            report.RetryCampaignAttemptCount++;
            report.FirstAttemptAt ??= current;
            report.RetryCampaignStartedAt ??= current;
            report.NextAttemptAt = null;
            report.LeaseOwner = Guid.NewGuid();
            report.LeaseUntil = Min(current + s_leaseDuration, report.ExpiresAt);
            report.CompletionToken = null;
            await db.SaveChangesAsync(ct);
            DateTimeOffset deadline = Min(report.LeaseUntil.Value, report.RetryCampaignStartedAt.Value + s_retryHorizon);
            if (deadline <= CurrentTime(now, startedAt))
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                continue;
            }
            await transaction.CommitAsync(ct);
            if (recovered)
                Observe(() => observer?.LeaseRecovered());
            if (deadline <= CurrentTime(now, startedAt))
            {
                db.ChangeTracker.Clear();
                continue;
            }
            return new CrashLease(report.Id, report.LeaseOwner.Value,
                new StoredCrashObject(report.ObjectKey, report.SourceETag, report.SourceSize, report.ReceivedAt),
                report.AttemptCount, report.ConsecutiveToolFailures, report.FirstAttemptAt.Value,
                report.RetryCampaignStartedAt.Value, report.ExpiresAt, report.LeaseUntil.Value)
            { RetryCampaignAttemptCount = report.RetryCampaignAttemptCount };
        }
    }

    public async Task<bool> RenewAsync(CrashLease lease, DateTimeOffset now, CancellationToken ct)
    {
        long startedAt = _timeProvider.GetTimestamp();
        now = now.ToUniversalTime();
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        CrashReport? report = await db.Reports.FromSql($"SELECT * FROM crash_reports WHERE id = {lease.ReportId.Value} FOR UPDATE").SingleOrDefaultAsync(ct);
        DateTimeOffset current = CurrentTime(now, startedAt);
        if (report is null || !OwnsCurrentLease(report, lease, current))
            return false;

        DateTimeOffset deadline = Min(report.LeaseUntil!.Value, report.ExpiresAt);
        report.LeaseUntil = Min(current + s_leaseDuration, report.ExpiresAt);
        await db.SaveChangesAsync(ct);
        if (deadline <= CurrentTime(now, startedAt))
            return false;

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> CompleteAsync(CrashLease lease, CrashAttemptResult result, DateTimeOffset now, CancellationToken ct)
    {
        long startedAt = _timeProvider.GetTimestamp();
        now = now.ToUniversalTime();
        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        // Completion and cleanup lock reports before groups; triage locks only its group row.
        CrashReport? report = await db.Reports.FromSql($"SELECT * FROM crash_reports WHERE id = {lease.ReportId.Value} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (report is null || report.ExpiresAt <= CurrentTime(now, startedAt) || !MatchesSource(report, lease.Source))
            return false;

        // Reconcile an acknowledgement lost after commit. The count was already committed with this token.
        if (report.CompletionToken == lease.OwnerToken)
            return true;

        if (!OwnsCurrentLease(report, lease, CurrentTime(now, startedAt)))
            return false;

        DateTimeOffset completionDeadline = Min(report.LeaseUntil!.Value, report.ExpiresAt);
        if (result.Status == CrashReportStatus.Processed && result.ProcessedDeadline is { } processedUntil)
            completionDeadline = Min(completionDeadline, processedUntil);
        TimeSpan remaining = completionDeadline - CurrentTime(now, startedAt);
        if (remaining <= TimeSpan.Zero)
            return false;
        // Acceptance/reconciliation is checked above. A new write cannot cross its ephemeral attempt deadline.
        using CancellationTokenSource? finalizationDeadline = result.ProcessedDeadline is null ? null : new(remaining, _timeProvider);
        using CancellationTokenSource? finalization = finalizationDeadline is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct, finalizationDeadline.Token);
        if (finalization is not null)
            ct = finalization.Token;

        bool groupCreated = false;
        if (result.Status == CrashReportStatus.Processed)
        {
            CrashAnalysis analysis = result.Analysis ?? throw new InvalidOperationException("Processed analysis is required.");
            SignatureIdentity signature = result.Signature ?? throw new InvalidOperationException("Processed signature is required.");
            (string stack, string meta) = SerializeAnalysis(analysis);
            CrashGroup group = await FindOrCreateGroupAsync(db, report, analysis.Metadata.Version, signature, ct);
            groupCreated = group.ReportCount == 0;
            if (completionDeadline <= CurrentTime(now, startedAt))
                return false;

            if (CrashVersion.Compare(analysis.Metadata.Version, analysis.Metadata.Version) is null
                || group.Status == CrashGroupStatus.Fixed && group.FixedInVersion is not null && CrashVersion.Compare(analysis.Metadata.Version, group.FixedInVersion) is null)
            {
                analysis = analysis with { Quality = analysis.Quality | CrashQualityFlags.VersionComparisonUnavailable };
                (stack, meta) = SerializeAnalysis(analysis);
            }
            UpdateGroup(group, report, analysis.Metadata.Version, now);
            SetAnalysis(report, analysis, signature, now, stack, meta);
            report.GroupId = group.Id;
            report.Status = CrashReportStatus.Processed;
            report.ReasonCode = null;
            report.ReasonDetail = null;
            report.ConsecutiveToolFailures = 0;
        }
        else
        {
            CrashFailureCode reason = result.Reason ?? throw new InvalidOperationException("Attempt reason is required.");
            if (IsToolFailure(reason))
                report.ConsecutiveToolFailures++;
            else if (result.Status == CrashReportStatus.RetryWait)
                report.ConsecutiveToolFailures = 0;

            CrashReportStatus status = result.Status;
            DateTimeOffset? next = result.NextAttemptAt?.ToUniversalTime();
            DateTimeOffset deadline = Min(report.ExpiresAt, report.RetryCampaignStartedAt!.Value + s_retryHorizon);
            if (status == CrashReportStatus.RetryWait)
            {
                if (report.ConsecutiveToolFailures >= 3 || now >= deadline || next is null || next >= deadline)
                {
                    status = CrashReportStatus.Failed;
                    next = null;
                }
                else
                {
                    next = Min(next.Value, deadline);
                }
            }
            FinishWithoutInput(report, status, reason, next);
        }
        report.LeaseOwner = null;
        report.LeaseUntil = null;
        if (result.Status == CrashReportStatus.Processed)
            report.NextAttemptAt = null;
        report.CompletionToken = lease.OwnerToken;
        await db.SaveChangesAsync(ct);
        if (completionDeadline <= CurrentTime(now, startedAt))
            return false;

        await transaction.CommitAsync(ct);
        if (report.Status == CrashReportStatus.RetryWait)
            Observe(() => observer?.Retry());
        else if (report.LastManualRetryAt is null)
            Observe(() => observer?.Terminal(report.Version ?? VersionOf(report.ObjectKey), report.Status));
        if (groupCreated)
            Observe(() => observer?.GroupCreated());
        return true;
    }

    private static string? VersionOf(string key) => CrashObjectKey.TryParse(key, out CrashObjectKey? parsed) ? parsed!.Version : null;

    private static void Observe(Action observation)
    {
        // Telemetry is best effort; exporter failures cannot change a committed queue outcome.
        try { observation(); }
        catch { }
    }

    private static async Task<CrashGroup> FindOrCreateGroupAsync(CrashesDbContext db, CrashReport report, string version, SignatureIdentity signature, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        // ON CONFLICT waits for an overlapping creator; the following lock observes its committed group.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO crash_groups (id, signature_version, signature_hash, canonical_signature, signature,
                first_seen, last_seen, report_count, first_version, last_version, first_report_id, last_report_id,
                status, is_regression, regression_count, triage_revision)
            VALUES ({id}, {signature.Version}, {signature.Hash}, {signature.Canonical}, {signature.Display},
                {report.ReceivedAt}, {report.ReceivedAt}, 0, {version}, {version}, {report.Id.Value}, {report.Id.Value}, 'new', false, 0, 0)
            ON CONFLICT (signature_version, signature_hash) DO NOTHING
            """, ct);
        CrashGroup group = await db.Groups.FromSql($"SELECT * FROM crash_groups WHERE signature_version = {signature.Version} AND signature_hash = {signature.Hash} FOR UPDATE").SingleAsync(ct);
        if (!string.Equals(group.CanonicalSignature, signature.Canonical, StringComparison.Ordinal))
            throw new InvalidOperationException("Crash signature hash collision.");

        return group;
    }

    private static void UpdateGroup(CrashGroup group, CrashReport report, string version, DateTimeOffset now)
    {
        group.ReportCount++;
        if (report.ReceivedAt < group.FirstSeen || report.ReceivedAt == group.FirstSeen && report.Id.Value.CompareTo(group.FirstReportId) < 0)
        {
            group.FirstSeen = report.ReceivedAt;
            group.FirstVersion = version;
            group.FirstReportId = report.Id.Value;
        }
        if (report.ReceivedAt > group.LastSeen || report.ReceivedAt == group.LastSeen && report.Id.Value.CompareTo(group.LastReportId) < 0)
        {
            group.LastSeen = report.ReceivedAt;
            group.LastVersion = version;
            group.LastReportId = report.Id.Value;
        }
        if (CrashRegression.ShouldReopen(group.Status, group.FixedInVersion, group.FixedAt, version, report.ReceivedAt))
        {
            group.Status = CrashGroupStatus.New;
            group.IsRegression = true;
            group.RegressionCount++;
            group.LastRegressionAt = now;
            group.TriageRevision++;
        }
    }

    private static (string Stack, string Meta) SerializeAnalysis(CrashAnalysis analysis)
    {
        // The producer's cap is compact serialized UTF-8. jsonb::text adds formatting and is not that representation.
        string stack = JsonSerializer.Serialize(analysis with { LogTail = null });
        string meta = JsonSerializer.Serialize(analysis.Metadata);
        if (Encoding.UTF8.GetByteCount(stack) > CrashLimits.StackBytes
            || Encoding.UTF8.GetByteCount(meta) > CrashLimits.MetadataBytes
            || analysis.LogTail is not null && Encoding.UTF8.GetByteCount(analysis.LogTail) > CrashLimits.LogBytes)
        {
            throw new CrashInputException(CrashFailureCode.InvalidAnalysis);
        }
        return (stack, meta);
    }

    private static void SetAnalysis(CrashReport report, CrashAnalysis analysis, SignatureIdentity signature, DateTimeOffset now, string stack, string meta)
    {
        report.Version = analysis.Metadata.Version;
        report.GitHash = analysis.Metadata.GitHash;
        report.Meta = meta;
        report.ClientCrashedAt = analysis.Metadata.Time.ToUniversalTime();
        report.ProcessedAt = now;
        report.SignatureVersion = signature.Version;
        report.ExceptionCode = analysis.Crash.Code;
        report.ExceptionAddress = analysis.Crash.Address;
        CrashFrame? fault = analysis.Threads.FirstOrDefault(x => x.Index == analysis.Crash.CrashingThreadIndex)?.Frames.FirstOrDefault();
        report.CrashingModule = fault?.ImageKey is not null ? fault.Module : null;
        report.OsVersion = analysis.System.OsVersion;
        report.OsBuild = analysis.System.OsBuild;
        report.Cpu = analysis.System.CpuArchitecture;
        CrashModule? driver;
        if (fault?.ImageKey is not null && CrashModuleRules.IsGpuDriver(fault.Module))
        {
            driver = analysis.Modules.FirstOrDefault(x => string.Equals(x.Name, fault.Module, StringComparison.OrdinalIgnoreCase) && string.Equals(x.ImageKey, fault.ImageKey, StringComparison.OrdinalIgnoreCase));
            report.GpuDriverModule = fault.Module;
            report.GpuDriverVersion = driver?.Version;
            report.GpuDriverSource = "faulting_module";
        }
        else
        {
            CrashModule[] candidates = analysis.Modules.Where(x => CrashModuleRules.IsGpuDriver(x.Name)).Take(2).ToArray();
            driver = candidates.Length == 1 ? candidates[0] : null;
            report.GpuDriverModule = driver?.Name;
            report.GpuDriverVersion = driver?.Version;
            report.GpuDriverSource = driver is null ? "unknown" : "loaded_module";
        }
        report.Stack = stack;
        report.StackSchemaVersion = analysis.SchemaVersion;
        report.QualityFlags = analysis.Quality;
        report.LogTail = analysis.LogTail;
        report.ProcessorVersion = analysis.Processor.StackwalkVersion;
        report.ConverterVersion = analysis.Processor.ConverterVersion;
        report.CacheVersion = analysis.Processor.CacheVersion;
    }

    private static void FinishWithoutInput(CrashReport report, CrashReportStatus status, CrashFailureCode reason, DateTimeOffset? next)
    {
        report.Status = status;
        report.ReasonCode = reason;
        report.ReasonDetail = null;
        report.NextAttemptAt = next;
        report.LeaseOwner = null;
        report.LeaseUntil = null;
        report.CompletionToken = null;
        report.GroupId = null;
        report.ProcessedAt = null;
        report.SignatureVersion = null;
        report.Version = null;
        report.GitHash = null;
        report.Meta = null;
        report.ClientCrashedAt = null;
        report.ExceptionCode = null;
        report.ExceptionAddress = null;
        report.CrashingModule = null;
        report.OsVersion = null;
        report.OsBuild = null;
        report.Cpu = null;
        report.GpuDriverModule = null;
        report.GpuDriverVersion = null;
        report.GpuDriverSource = null;
        report.Stack = null;
        report.StackSchemaVersion = null;
        report.QualityFlags = CrashQualityFlags.None;
        report.LogTail = null;
        report.ProcessorVersion = null;
        report.ConverterVersion = null;
        report.CacheVersion = null;
    }

    private static bool MatchesSource(CrashReport report, StoredCrashObject source) =>
        string.Equals(report.ObjectKey, source.Key, StringComparison.Ordinal)
        && string.Equals(report.SourceETag, source.ETag, StringComparison.Ordinal)
        && report.SourceSize == source.Size && report.ReceivedAt == source.LastModified;

    private DateTimeOffset CurrentTime(DateTimeOffset now, long startedAt) => now + _timeProvider.GetElapsedTime(startedAt);

    private static bool OwnsCurrentLease(CrashReport report, CrashLease lease, DateTimeOffset now) =>
        report.Status == CrashReportStatus.Processing && report.LeaseOwner == lease.OwnerToken
        && report.LeaseUntil > now && report.ExpiresAt > now && MatchesSource(report, lease.Source);

    private static bool IsActive(CrashReportStatus status) => status is CrashReportStatus.Pending or CrashReportStatus.Processing or CrashReportStatus.RetryWait;
    private static bool IsToolFailure(CrashFailureCode reason) => reason is CrashFailureCode.ToolTimeout or CrashFailureCode.ToolFailed or CrashFailureCode.ResourceLimit;
    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
}
