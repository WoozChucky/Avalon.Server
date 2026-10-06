using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public sealed record LicenseHoldResult(bool Changed, long AuthorityRevision, DateTime? SuspendedAt);

public interface ILicenseHoldRepository
{
    Task<LicenseHoldResult> SetAsync(Guid licenseId, string causeKind, string causeReference, bool active, DateTime observedAt, CancellationToken ct = default);
}

public sealed class LicenseHoldRepository(IDbContextFactory<AuthDbContext> factory) : ILicenseHoldRepository
{
    public async Task<LicenseHoldResult> SetAsync(Guid licenseId, string causeKind, string causeReference, bool active, DateTime observedAt, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await LicenseHoldMutations.SetAsync(db, licenseId, causeKind, causeReference, active, observedAt, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}

/// <summary>Stages mutations in the caller's transaction, allowing payment state and holds to commit together.</summary>
public static class LicenseHoldMutations
{
    public static async Task<LicenseHoldResult> SetAsync(AuthDbContext db, Guid licenseId, string causeKind, string causeReference,
        bool active, DateTime observedAt, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("License holds require an owned transaction.");
        if (licenseId == Guid.Empty || causeKind is not ("support" or "payment-dispute") || string.IsNullOrWhiteSpace(causeReference) ||
            causeReference != causeReference.Trim() || causeReference.Length > 256 || observedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid license hold cause.");
        if (await db.GameLicenses.Where(x => x.Id == licenseId).ExecuteUpdateAsync(u => u.SetProperty(x => x.AuthorityRevision, x => x.AuthorityRevision), ct) != 1)
            throw new InvalidOperationException("License not found.");
        var license = await db.GameLicenses.SingleAsync(x => x.Id == licenseId, ct);
        if (observedAt < license.GrantedAt || license.AuthorityRevision == long.MaxValue) throw new InvalidOperationException("License authority cannot be changed.");
        var rows = await db.LicenseHolds.Where(x => x.LicenseId == licenseId).ToListAsync(ct);
        // A reconciliation can stage several causes before its single SaveChanges.
        rows.AddRange(db.LicenseHolds.Local.Where(x => x.LicenseId == licenseId && !rows.Contains(x)).ToList());
        var hold = rows.SingleOrDefault(x => x.CauseKind == causeKind && x.CauseReference == causeReference);
        if (hold is null && !active || hold is not null &&
            (active == (hold.ReleasedAt is null) || observedAt < hold.StartedAt || active && observedAt <= hold.ReleasedAt))
            return new(false, license.AuthorityRevision, license.SuspendedAt);
        if (hold is null)
        {
            hold = new LicenseHold { Id = Guid.NewGuid(), LicenseId = licenseId, CauseKind = causeKind, CauseReference = causeReference, StartedAt = observedAt };
            rows.Add(hold);
            db.LicenseHolds.Add(hold);
        }
        else if (active) { hold.StartedAt = observedAt; hold.ReleasedAt = null; }
        else hold.ReleasedAt = observedAt;
        license.SuspendedAt = rows.Where(x => x.ReleasedAt is null).Select(x => (DateTime?)x.StartedAt).Min();
        license.AuthorityRevision++;
        return new(true, license.AuthorityRevision, license.SuspendedAt);
    }
}
