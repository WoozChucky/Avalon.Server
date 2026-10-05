using Avalon.Common.GameAuth;

namespace Avalon.Infrastructure.StoreAuth;

public sealed class SteamLicenseProvider(ISteamOwnershipClient ownership) : IGameLicenseProvider
{
    public string Provider => StoreProviders.Steam;
    public LicenseAuthorityKind AuthorityKind => LicenseAuthorityKind.VerifiedOwnership;
    public async Task<GameLicenseCheckResult> CheckAsync(GameLicenseCheckRequest request, CancellationToken ct)
    {
        var reference = request.Identity is null ? string.Empty : $"ownership:{request.Application.ProviderProductId}:{request.Identity.ProviderSubject}";
        if (request.Identity is null) return new(GameLicenseCheckStatus.Unavailable, reference, request.Now, request.Now);
        var result = await ownership.CheckAsync(SteamIdentityProvider.AppId(request.Application), request.Identity.ProviderSubject, ct);
        return new(result.Status switch
        {
            SteamOwnershipStatus.Owned => GameLicenseCheckStatus.Licensed,
            SteamOwnershipStatus.NotOwned => GameLicenseCheckStatus.Unlicensed,
            _ => GameLicenseCheckStatus.Unavailable,
        }, reference, result.ObservedAt, result.AuthorizedUntil, result.ProviderExpiresAt,
            ProviderProductId: request.Application.ProviderProductId, ProviderSubject: result.ProviderSubject,
            OwnerSubject: result.OwnerSubject, Permanent: result.Permanent);
    }
}
