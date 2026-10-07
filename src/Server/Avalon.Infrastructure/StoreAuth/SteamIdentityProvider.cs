using System.Globalization;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.StoreAuth;

public sealed class SteamIdentityProvider(ISteamProofVerifier verifier, IOptions<StoreAuthenticationConfiguration> options,
    TimeProvider clock) : IGameIdentityProvider
{
    public string Provider => StoreProviders.Steam;
    public string CanonicalProof(string proof) => !string.IsNullOrEmpty(proof) &&
        proof.Length <= GameAuthPolicy.MaximumSteamTicketHexCharacters && proof.Length % 2 == 0 && proof.All(Uri.IsHexDigit)
        ? proof.ToUpperInvariant() : throw new ArgumentException("Invalid Steam proof shape.");
    public string CreateChallenge(GameApplicationSelection application) => SteamTicketIdentity.Create(options.Value, AppId(application));
    public async Task<GameIdentityProofResult> VerifyAsync(GameIdentityProofRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.Proof) || request.Proof.Length > GameAuthPolicy.MaximumSteamTicketHexCharacters ||
            request.Proof.Length % 2 != 0 || !request.Proof.All(Uri.IsHexDigit))
        {
            return new(GameIdentityProofStatus.Invalid);
        }

        SteamProofResult result = await verifier.VerifyAsync(AppId(request.Application), request.Proof, request.ExpectedChallenge, ct);
        DateTime now = clock.GetUtcNow().UtcDateTime;
        return result.Status switch
        {
            SteamProofStatus.Verified when result.ProviderSubject is not null =>
                new(GameIdentityProofStatus.Verified, new(result.ProviderSubject, now, now.Add(GameAuthPolicy.IdentityLifetime))),
            SteamProofStatus.ProviderUnavailable => new(GameIdentityProofStatus.Unavailable),
            _ => new(GameIdentityProofStatus.Invalid),
        };
    }
    internal static uint AppId(GameApplicationSelection app) => app.Provider == StoreProviders.Steam &&
        uint.TryParse(app.ProviderProductId, NumberStyles.None, CultureInfo.InvariantCulture, out uint id) && id != 0 &&
        id.ToString(CultureInfo.InvariantCulture) == app.ProviderProductId ? id : throw new ArgumentException("Invalid Steam application binding.");
}
