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
    Task<GameLicense?> ApplyDecisionAsync(Guid id, long expectedRevision, LicenseAuthorityDecision decision, CancellationToken ct = default);
}

public sealed class GameLicenseRepository(IDbContextFactory<AuthDbContext> factory) : IGameLicenseRepository
{
    public async Task<GameLicense?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.GameLicenses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<GameLicense?> FindAsync(AccountId account, string provider, string environment, string reference, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.GameLicenses.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account &&
            x.Provider == provider && x.Environment == environment && x.LicenseReference == reference, ct);
    }

    public async Task<GameLicense?> FindActiveAsync(AccountId account, string provider, string environment, string product,
        string providerProductId, DateTime now, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.GameLicenses.AsNoTracking().Where(x => x.AccountId == account && x.Provider == provider &&
            x.Environment == environment && x.Product == product && x.ProviderProductId == providerProductId &&
            x.RevokedAt == null && x.GrantedAt <= now && (x.ExpiresAt == null || x.ExpiresAt > now))
            .OrderBy(x => x.GrantedAt).ThenBy(x => x.Id).ToListAsync(ct);
        return rows.FirstOrDefault(x => x.Authorizes(account, product, environment, now));
    }

    public async Task<GameLicense> RecordGrantAsync(GameLicense license, CancellationToken ct = default)
    {
        Validate(license);
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.GameLicenses.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == license.Provider &&
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

    public async Task<GameLicense?> ApplyDecisionAsync(Guid id, long expectedRevision, LicenseAuthorityDecision decision, CancellationToken ct = default)
    {
        if (decision.ObservedAt.Kind != DateTimeKind.Utc || decision.AuthorizedUntil.Kind != DateTimeKind.Utc ||
            decision.AuthorizedUntil > decision.ObservedAt.AddMinutes(5) ||
            (decision.ProviderExpiresAt is { } expiry && (expiry.Kind != DateTimeKind.Utc || decision.AuthorizedUntil > expiry)) ||
            (!decision.OwnsProduct && decision.AuthorizedUntil > decision.ObservedAt))
            throw new ArgumentException("Invalid bounded license decision.", nameof(decision));
        await using var db = await factory.CreateDbContextAsync(ct);
        var license = await db.GameLicenses.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (license is null || license.AuthorityRevision != expectedRevision || expectedRevision == long.MaxValue ||
            decision.ObservedAt < license.GrantedAt || decision.ObservedAt < license.LastObservedAt) return null;
        if (license.RevokedAt is not null && decision.OwnsProduct)
        {
            if (license.AuthorityKind == LicenseAuthorityKind.StoredGrant || !decision.Reestablish ||
                decision.ObservedAt <= license.LastObservedAt) return null;
            license.AuthorityRevision++;
            license.RevokedAt = null;
        }
        else if (!decision.OwnsProduct && license.RevokedAt is null)
        {
            license.AuthorityRevision++;
            license.RevokedAt = decision.ObservedAt;
        }
        if (decision.OwnsProduct && decision.ObservedAt == license.LastObservedAt &&
            decision.AuthorizedUntil != license.VerifiedUntil) return null;
        license.LastObservedAt = decision.ObservedAt;
        license.VerifiedUntil = decision.OwnsProduct ? decision.AuthorizedUntil : null;
        if (license.AuthorityKind == LicenseAuthorityKind.VerifiedOwnership) license.ExpiresAt = decision.ProviderExpiresAt;
        try { await db.SaveChangesAsync(ct); return license; }
        catch (DbUpdateConcurrencyException) { return null; }
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
            throw new ArgumentException("Invalid source-bound license.", nameof(license));
    }
}
