using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.StoreAuth;

public sealed class SteamProofVerifier(HttpClient client, IOptions<StoreAuthenticationConfiguration> options) : ISteamProofVerifier
{
    public async Task<SteamProofResult> VerifyAsync(string ticketHex, string expectedIdentity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = options.Value;
        config.Validate(string.Equals(config.Environment, "production", StringComparison.Ordinal));
        var prefix = config.SteamIdentityPrefix + ":";
        if (string.IsNullOrEmpty(ticketHex) || ticketHex.Length > 5120 || ticketHex.Length % 2 != 0 ||
            !ticketHex.All(Uri.IsHexDigit) || expectedIdentity is null ||
            !expectedIdentity.StartsWith(prefix, StringComparison.Ordinal) ||
            expectedIdentity.Length != prefix.Length + 32 || !expectedIdentity.AsSpan(prefix.Length).ToArray().All(Uri.IsHexDigit))
            return new(SteamProofStatus.InvalidProof);
        var uri = SteamWebApi.Request("ISteamUserAuth/AuthenticateUserTicket/v1/", config,
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
