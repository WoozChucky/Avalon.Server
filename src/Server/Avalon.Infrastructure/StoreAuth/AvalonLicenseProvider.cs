using Avalon.Common.GameAuth;
using Avalon.Database.Auth.Repositories;

namespace Avalon.Infrastructure.StoreAuth;

/// <summary>Reads fulfilled Avalon grants after launcher account/family authority has been validated.</summary>
public sealed class AvalonLicenseProvider(IGameLicenseRepository licenses) : IGameLicenseProvider
{
    public string Provider => StoreProviders.Avalon;
    public LicenseAuthorityKind AuthorityKind => LicenseAuthorityKind.StoredGrant;
    public async Task<GameLicenseCheckResult> CheckAsync(GameLicenseCheckRequest request, CancellationToken ct)
    {
        if (request.Application.Provider != Provider || request.Identity is not null)
            return new(GameLicenseCheckStatus.Unavailable, string.Empty, request.Now, request.Now);
        var row = request.BoundLicenseId is { } id ? await licenses.FindAsync(id, ct) :
            await licenses.FindActiveAsync(request.Account, Provider, request.Application.Environment,
                request.Application.Product, request.Application.ProviderProductId, request.Now, ct);
        if (row is null) return new(GameLicenseCheckStatus.Unlicensed, string.Empty, request.Now, request.Now);
        if (row.AccountId != request.Account || row.Provider != Provider || row.Product != request.Application.Product ||
            row.Environment != request.Application.Environment || row.ProviderProductId != request.Application.ProviderProductId ||
            row.ProviderSubject is not null || row.AuthorityKind != AuthorityKind ||
            (request.BoundRevision is { } revision && row.AuthorityRevision != revision))
            return new(GameLicenseCheckStatus.Unavailable, string.Empty, request.Now, request.Now);
        var owned = row.Authorizes(request.Account, request.Application.Product, request.Application.Environment, request.Now);
        var until = owned ? request.Now.Add(GameAuthPolicy.OwnershipLifetime) : request.Now;
        if (owned && row.ExpiresAt is { } expiry && expiry < until) until = expiry;
        return new(owned ? GameLicenseCheckStatus.Licensed : GameLicenseCheckStatus.Unlicensed,
            row.LicenseReference, request.Now, until, row.ExpiresAt, row.Id, row.ProviderProductId);
    }
}
