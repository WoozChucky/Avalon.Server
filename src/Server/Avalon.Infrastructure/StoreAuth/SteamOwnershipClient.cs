using System.Globalization;
using System.Text.Json;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.StoreAuth;

public sealed class SteamOwnershipClient(HttpClient client, IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock)
    : ISteamOwnershipClient
{
    public async Task<SteamOwnershipResult> CheckAsync(uint appId, string verifiedSteamId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StoreAuthenticationConfiguration config = options.Value;
        config.Validate(string.Equals(config.Environment, "production", StringComparison.Ordinal));
        DateTime observed = clock.GetUtcNow().UtcDateTime;
        if (config.ResolveSteamApplication(appId) is null || !SteamWebApi.IsSteamId(verifiedSteamId)) return Unavailable(verifiedSteamId, observed);
        Uri uri = SteamWebApi.Request(SteamWebApi.CheckOwnershipPath, config, appId, ("steamid", verifiedSteamId));
        (bool available, JsonDocument? document) = await SteamWebApi.GetAsync(client, uri, cancellationToken);
        using (document)
        {
            if (!available || document is null || !SteamWebApi.Object(document.RootElement, "appownership", out JsonElement ownership))
                return Unavailable(verifiedSteamId, observed);
            bool? owns = SteamWebApi.Boolean(ownership, "ownsapp");
            if (owns is null) return Unavailable(verifiedSteamId, observed);
            if (!owns.Value) return new(SteamOwnershipStatus.NotOwned, verifiedSteamId, observed, observed);
            string? expires = SteamWebApi.String(ownership, "timeexpires");
            if (!TryExpiry(expires, out DateTime? providerExpiry)) return Unavailable(verifiedSteamId, observed);
            DateTime until = observed.Add(GameAuthPolicy.OwnershipLifetime);
            if (providerExpiry is { } expiry && expiry < until) until = expiry;
            if (until <= observed) return new(SteamOwnershipStatus.NotOwned, verifiedSteamId, observed, observed, providerExpiry);
            string? owner = SteamWebApi.String(ownership, "ownersteamid");
            if (owner is not null && !SteamWebApi.IsSteamId(owner)) return Unavailable(verifiedSteamId, observed);
            return new(SteamOwnershipStatus.Owned, verifiedSteamId, observed, until, providerExpiry, owner,
                SteamWebApi.Boolean(ownership, "permanent"));
        }
    }

    private static SteamOwnershipResult Unavailable(string subject, DateTime now) =>
        new(SteamOwnershipStatus.ProviderUnavailable, subject, now, now);

    private static bool TryExpiry(string? text, out DateTime? expires)
    {
        expires = null;
        if (string.Equals(text, "never", StringComparison.Ordinal)) return true;
        if (!DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
            return false;
        expires = parsed.UtcDateTime;
        return true;
    }
}
