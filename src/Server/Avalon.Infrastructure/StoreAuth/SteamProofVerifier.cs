using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.StoreAuth;

public sealed class SteamProofVerifier(HttpClient client, IOptions<StoreAuthenticationConfiguration> options) : ISteamProofVerifier
{
    public async Task<SteamProofResult> VerifyAsync(uint appId, string ticketHex, string expectedIdentity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = options.Value;
        config.Validate(string.Equals(config.Environment, "production", StringComparison.Ordinal));
        var prefix = config.SteamIdentityPrefix + ":" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":";
        if (config.ResolveSteamApplication(appId) is null || string.IsNullOrEmpty(ticketHex) || ticketHex.Length > GameAuthPolicy.MaximumSteamTicketHexCharacters || ticketHex.Length % 2 != 0 ||
            !ticketHex.All(Uri.IsHexDigit) || expectedIdentity is null ||
            !expectedIdentity.StartsWith(prefix, StringComparison.Ordinal) ||
            expectedIdentity.Length != prefix.Length + 32 || !expectedIdentity.AsSpan(prefix.Length).ToArray().All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return new(SteamProofStatus.InvalidProof);
        var uri = SteamWebApi.Request(SteamWebApi.AuthenticateTicketPath, config, appId,
            ("ticket", ticketHex), ("identity", expectedIdentity));
        var (available, document) = await SteamWebApi.GetAsync(client, uri, cancellationToken);
        using (document)
        {
            if (!available) return new(SteamProofStatus.ProviderUnavailable);
            if (document is null || !SteamWebApi.Object(document.RootElement, "response", out var response) ||
                response.TryGetProperty("error", out _) || !SteamWebApi.Object(response, "params", out var parameters) ||
                !string.Equals(SteamWebApi.String(parameters, "result"), "OK", StringComparison.Ordinal))
                return new(SteamProofStatus.InvalidProof);
            var subject = SteamWebApi.String(parameters, "steamid");
            return SteamWebApi.IsSteamId(subject) ? new(SteamProofStatus.Verified, subject) : new(SteamProofStatus.InvalidProof);
        }
    }
}
