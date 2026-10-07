using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Auth.Repositories;

public interface ILicenseObservationRepository
{
    Task<bool> HasNegativeSinceAsync(AccountId accountId, string provider, string subject, string environment,
        string product, string providerAppId, DateTime since, CancellationToken cancellationToken = default);
    Task RecordAsync(LicenseObservation observation, CancellationToken cancellationToken = default);
    Task<LicenseObservation?> FindLatestAsync(AccountId accountId, string provider, string subject,
        string environment, string product, string providerAppId, CancellationToken cancellationToken = default);
}

public sealed class LicenseObservationRepository(IDbContextFactory<AuthDbContext> factory) : ILicenseObservationRepository
{
    public async Task<bool> HasNegativeSinceAsync(AccountId accountId, string provider, string subject, string environment,
        string product, string providerAppId, DateTime since, CancellationToken cancellationToken = default)
    {
        await using AuthDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.LicenseObservations.AnyAsync(x => x.AccountId == accountId && x.Provider == provider &&
            x.ProviderSubject == subject && x.Environment == environment && x.Product == product && x.ProviderProductId == providerAppId && !x.OwnsProduct && x.ObservedAt >= since,
            cancellationToken);
    }
    public async Task RecordAsync(LicenseObservation observation, CancellationToken cancellationToken = default)
    {
        if (observation.AuthorizedUntil > observation.ObservedAt.AddMinutes(5) ||
            (observation.ProviderExpiresAt is { } expiry && observation.AuthorizedUntil > expiry) ||
            (!observation.OwnsProduct && observation.AuthorizedUntil > observation.ObservedAt))
        {
            throw new ArgumentException("License authority exceeds the provider evidence deadline.", nameof(observation));
        }

        await using AuthDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        db.LicenseObservations.Add(observation);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LicenseObservation?> FindLatestAsync(AccountId accountId, string provider, string subject,
        string environment, string product, string providerAppId, CancellationToken cancellationToken = default)
    {
        await using AuthDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.LicenseObservations.AsNoTracking().Where(x => x.AccountId == accountId &&
            x.Provider == provider && x.ProviderSubject == subject && x.Environment == environment && x.Product == product && x.ProviderProductId == providerAppId)
            .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
    }
}
