using System.Text.RegularExpressions;
using Avalon.Crashes;
using Avalon.Crashes.Analysis;
using Avalon.Crashes.Triage;
using Avalon.Database.Crashes.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avalon.Database.Crashes;

public sealed class CrashTriageStore(IDbContextFactory<CrashesDbContext> factory) : ICrashTriageStore
{
    private static readonly Regex s_issueUrl = new(@"\A(?i:https://github\.com)/[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*/[A-Za-z0-9_.-]+/issues/[0-9]+\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public async Task<CrashTriageResult> UpdateAsync(CrashGroupId id, CrashGroupPatch patch, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ct.ThrowIfCancellationRequested();
        if (patch.TriageRevision < 0)
            return CrashTriageResult.Invalid(CrashTriageValidationCode.InvalidRevision);
        if (!patch.Status.IsSpecified && !patch.FixedInVersion.IsSpecified && !patch.IssueUrl.IsSpecified && !patch.Notes.IsSpecified)
            return CrashTriageResult.Invalid(CrashTriageValidationCode.EmptyPatch);
        if (patch.Status.IsSpecified && (patch.Status.Value is not { } status || !Enum.IsDefined(status)))
            return CrashTriageResult.Invalid(CrashTriageValidationCode.InvalidStatus);
        if (patch.FixedInVersion.IsSpecified && patch.FixedInVersion.Value is { } version && !ValidVersion(version))
            return CrashTriageResult.Invalid(CrashTriageValidationCode.InvalidFixedVersion);
        if (patch.IssueUrl.IsSpecified && patch.IssueUrl.Value is { } issueUrl && !ValidIssueUrl(issueUrl))
            return CrashTriageResult.Invalid(CrashTriageValidationCode.InvalidIssueUrl);
        if (patch.Notes.IsSpecified && patch.Notes.Value is { Length: > 4096 })
            return CrashTriageResult.Invalid(CrashTriageValidationCode.NotesTooLong);
        if (patch.Notes.IsSpecified && patch.Notes.Value?.Contains('\0') == true)
            return CrashTriageResult.Invalid(CrashTriageValidationCode.InvalidNotes);

        await using CrashesDbContext db = await factory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(5));
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        // Finalization locks this same row before updating aggregates and automatic regression state.
        CrashGroup? group = await db.Groups.FromSql($"SELECT * FROM crash_groups WHERE id = {id.Value} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (group is null)
            return CrashTriageResult.Missing();
        if (group.TriageRevision != patch.TriageRevision)
            return CrashTriageResult.Conflict(group.TriageRevision);

        CrashGroupStatus nextStatus = patch.Status.IsSpecified ? patch.Status.Value!.Value : group.Status;
        string? nextVersion = patch.FixedInVersion.IsSpecified ? patch.FixedInVersion.Value : group.FixedInVersion;
        if (nextStatus == CrashGroupStatus.Fixed && (nextVersion is null || !ValidVersion(nextVersion)))
            return CrashTriageResult.Invalid(CrashTriageValidationCode.InvalidFixedVersion);

        bool explicitFix = patch.Status.IsSpecified && nextStatus == CrashGroupStatus.Fixed;
        bool changedFixedVersion = nextStatus == CrashGroupStatus.Fixed && patch.FixedInVersion.IsSpecified
            && !string.Equals(nextVersion, group.FixedInVersion, StringComparison.Ordinal);
        now = now.ToUniversalTime();
        group.Status = nextStatus;
        group.FixedInVersion = nextVersion;
        if (patch.IssueUrl.IsSpecified)
            group.IssueUrl = patch.IssueUrl.Value;
        if (patch.Notes.IsSpecified)
            group.Notes = patch.Notes.Value;
        if (explicitFix || changedFixedVersion)
            group.FixedAt = now;
        if (patch.Status.IsSpecified && nextStatus is CrashGroupStatus.Fixed or CrashGroupStatus.Ignored)
            group.IsRegression = false;
        group.TriagedAt = now;
        group.TriageRevision = checked(group.TriageRevision + 1);

        // A tracked locked entity writes only changed triage columns; aggregates/history are never assigned.
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return CrashTriageResult.Updated(new(group.Id, group.Status, group.FixedInVersion, group.IssueUrl, group.Notes,
            group.IsRegression, group.RegressionCount, group.LastRegressionAt, group.TriageRevision, group.TriagedAt, group.FixedAt));
    }

    private static bool ValidVersion(string version) => version.Length <= 64 && CrashVersion.Compare(version, version) is not null;

    private static bool ValidIssueUrl(string value) => value.Length <= 2048 && s_issueUrl.IsMatch(value)
        && value[(value.LastIndexOf('/') + 1)..].Any(c => c != '0')
        && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && string.Equals(uri.AbsolutePath, value["https://github.com".Length..], StringComparison.Ordinal);
}
