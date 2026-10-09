using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface IGameLicenseRepository
{
    Task<GameLicense?> FindAsync(Guid id, CancellationToken ct = default);
    Task<GameLicense?> FindAsync(AccountId account, string provider, string environment, string reference, CancellationToken ct = default);
    Task<GameLicense?> FindActiveAsync(AccountId account, string provider, string environment, string product, string providerProductId, DateTime now, CancellationToken ct = default);
    Task<GameLicense> RecordGrantAsync(GameLicense license, CancellationToken ct = default);
    Task<LicenseDecisionResult> ApplyDecisionAsync(Guid id, long expectedRevision, LicenseAuthorityDecision decision, CancellationToken ct = default);
}

/// <summary>Why a license decision was or was not applied.</summary>
public enum LicenseDecisionOutcome
{
    /// <summary>The decision was applied; <see cref="LicenseDecisionResult.License"/> is the row after it.</summary>
    Applied,

    /// <summary>The decision cannot apply to this row (gone, revoked for good, evidence older than the grant).</summary>
    Refused,

    /// <summary>Another decision was applied first (revision, newer observation or the row's concurrency token).</summary>
    Conflict,
}

public readonly record struct LicenseDecisionResult(GameLicense? License, LicenseDecisionOutcome Outcome)
{
    public static LicenseDecisionResult Refused => new(null, LicenseDecisionOutcome.Refused);
    public static LicenseDecisionResult Conflict => new(null, LicenseDecisionOutcome.Conflict);
}

public sealed class GameLicenseRepository(IDbContextFactory<AuthDbContext> factory) : IGameLicenseRepository
{
    public async Task<GameLicense?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using AuthDbContext db = await factory.CreateDbContextAsync(ct);
        return await db.GameLicenses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<GameLicense?> FindAsync(AccountId account, string provider, string environment, string reference, CancellationToken ct = default)
    {
        await using AuthDbContext db = await factory.CreateDbContextAsync(ct);
        return await db.GameLicenses.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account &&
            x.Provider == provider && x.Environment == environment && x.LicenseReference == reference, ct);
    }

    public async Task<GameLicense?> FindActiveAsync(AccountId account, string provider, string environment, string product,
        string providerProductId, DateTime now, CancellationToken ct = default)
    {
        await using AuthDbContext db = await factory.CreateDbContextAsync(ct);
        List<GameLicense> rows = await db.GameLicenses.AsNoTracking().Where(x => x.AccountId == account && x.Provider == provider &&
            x.Environment == environment && x.Product == product && x.ProviderProductId == providerProductId &&
            x.RevokedAt == null && x.SuspendedAt == null && x.GrantedAt <= now && (x.ExpiresAt == null || x.ExpiresAt > now))
            .OrderBy(x => x.GrantedAt).ThenBy(x => x.Id).ToListAsync(ct);
        return rows.FirstOrDefault(x => x.Authorizes(account, product, environment, now));
    }

    public async Task<GameLicense> RecordGrantAsync(GameLicense license, CancellationToken ct = default)
    {
        Validate(license);
        await using AuthDbContext db = await factory.CreateDbContextAsync(ct);
        GameLicense? existing = await db.GameLicenses.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == license.Provider &&
            x.Environment == license.Environment && x.LicenseReference == license.LicenseReference, ct);
        if (existing is not null) return Match(existing, license);
        db.GameLicenses.Add(license);
        try { await db.SaveChangesAsync(ct); return license; }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.GameLicenses.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == license.Provider &&
                x.Environment == license.Environment && x.LicenseReference == license.LicenseReference, ct);
            if (existing is null) throw;
            return Match(existing, license);
        }
    }

    /// <summary>
    /// Records new grants on a context the caller owns, so they commit with the accounts they are for, each validated as
    /// <see cref="RecordGrantAsync"/> validates it. Unlike it, a reference already recorded is not matched against: the
    /// unique reference refuses it, and the caller's transaction with it, so the caller's references must be new.
    /// </summary>
    public static async Task RecordNewGrantsAsync(AuthDbContext db, IReadOnlyList<GameLicense> licenses, CancellationToken ct = default)
    {
        foreach (GameLicense license in licenses)
            Validate(license);
        db.GameLicenses.AddRange(licenses);
        await db.SaveChangesAsync(ct);
    }

    public async Task<LicenseDecisionResult> ApplyDecisionAsync(Guid id, long expectedRevision, LicenseAuthorityDecision decision, CancellationToken ct = default)
    {
        if (decision.ObservedAt.Kind != DateTimeKind.Utc || decision.AuthorizedUntil.Kind != DateTimeKind.Utc ||
            decision.AuthorizedUntil > decision.ObservedAt.AddMinutes(5) ||
            (decision.ProviderExpiresAt is { } expiry && (expiry.Kind != DateTimeKind.Utc || decision.AuthorizedUntil > expiry)) ||
            (!decision.OwnsProduct && decision.AuthorizedUntil > decision.ObservedAt))
        {
            throw new ArgumentException("Invalid bounded license decision.", nameof(decision));
        }

        await using AuthDbContext db = await factory.CreateDbContextAsync(ct);
        GameLicense? license = await db.GameLicenses.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (license is null || expectedRevision == long.MaxValue || decision.ObservedAt < license.GrantedAt)
            return LicenseDecisionResult.Refused;
        // A moved revision or a newer observation means another decision was applied first: the caller keeps what that
        // decision left standing and asks again with fresh evidence, rather than taking this for a refusal.
        if (license.AuthorityRevision != expectedRevision || decision.ObservedAt < license.LastObservedAt)
            return LicenseDecisionResult.Conflict;

        if (license.RevokedAt is not null && decision.OwnsProduct)
        {
            if (license.AuthorityKind == LicenseAuthorityKind.StoredGrant || !decision.Reestablish ||
                decision.ObservedAt <= license.LastObservedAt)
            {
                return LicenseDecisionResult.Refused;
            }

            license.AuthorityRevision++;
            license.RevokedAt = null;
        }
        else if (!decision.OwnsProduct && license.RevokedAt is null)
        {
            license.AuthorityRevision++;
            license.RevokedAt = decision.ObservedAt;
        }
        if (decision.OwnsProduct && decision.ObservedAt == license.LastObservedAt &&
            decision.AuthorizedUntil != license.VerifiedUntil)
        {
            return LicenseDecisionResult.Conflict;
        }

        license.LastObservedAt = decision.ObservedAt;
        license.VerifiedUntil = decision.OwnsProduct ? decision.AuthorizedUntil : null;
        // Negative evidence invalidates the revision; its expiry remains audit evidence and
        // must not rewrite the original grant interval (it may predate that grant).
        if (decision.OwnsProduct && license.AuthorityKind == LicenseAuthorityKind.VerifiedOwnership)
            license.ExpiresAt = decision.ProviderExpiresAt;
        try { await db.SaveChangesAsync(ct); return new(license, LicenseDecisionOutcome.Applied); }
        catch (DbUpdateConcurrencyException) { return LicenseDecisionResult.Conflict; }
    }

    private static GameLicense Match(GameLicense existing, GameLicense proposed) =>
        existing.AccountId == proposed.AccountId && existing.Product == proposed.Product &&
        existing.ProviderProductId == proposed.ProviderProductId && existing.ProviderSubject == proposed.ProviderSubject &&
        existing.AuthorityKind == proposed.AuthorityKind ? existing : throw new InvalidOperationException("License evidence is already bound.");

    private static void Validate(GameLicense license)
    {
        static bool Text(string value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
        if (license.Id == Guid.Empty || license.AccountId is null || license.AccountId.Value <= 0 || license.AuthorityRevision != 1 ||
            !Text(license.Provider, 32) || !Text(license.Environment, 32) || !Text(license.Product, 128) ||
            !Text(license.ProviderProductId, 128) || !Text(license.LicenseReference, 256) ||
            !Enum.IsDefined(license.AuthorityKind) || license.GrantedAt.Kind != DateTimeKind.Utc ||
            (license.ExpiresAt is { } expiry && (expiry.Kind != DateTimeKind.Utc || expiry <= license.GrantedAt)) ||
            (license.RevokedAt is { } revoked && (revoked.Kind != DateTimeKind.Utc || revoked < license.GrantedAt)) ||
            license.LastObservedAt is not null || license.VerifiedUntil is not null)
        {
            throw new ArgumentException("Invalid source-bound license.", nameof(license));
        }
    }
}
