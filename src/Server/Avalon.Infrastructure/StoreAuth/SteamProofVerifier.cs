using System.Text.Json;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.StoreAuth;

public sealed class SteamProofVerifier(HttpClient client, IOptions<StoreAuthenticationConfiguration> options,
    ILogger<SteamProofVerifier>? logger = null) : ISteamProofVerifier
{
    public async Task<SteamProofResult> VerifyAsync(uint appId, string ticketHex, string expectedIdentity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StoreAuthenticationConfiguration config = options.Value;
        config.Validate(string.Equals(config.Environment, "production", StringComparison.Ordinal));
        string? prefix = SteamTicketIdentity.Prefix(config, appId);
        if (prefix is null || string.IsNullOrEmpty(ticketHex) || ticketHex.Length > GameAuthPolicy.MaximumSteamTicketHexCharacters || ticketHex.Length % 2 != 0 ||
            !ticketHex.All(Uri.IsHexDigit) || expectedIdentity is null ||
            !expectedIdentity.StartsWith(prefix, StringComparison.Ordinal) ||
            expectedIdentity.Length != prefix.Length + SteamTicketIdentity.NonceCharacters ||
            !expectedIdentity.AsSpan(prefix.Length).ToArray().All(c => c is >= 'a' and <= 'z' or >= '2' and <= '7'))
            return Rejected(appId, "invalid_input");
        Uri uri = SteamWebApi.Request(SteamWebApi.AuthenticateTicketPath, config, appId,
            ("ticket", ticketHex), ("identity", expectedIdentity));
        (bool available, JsonDocument? document) = await SteamWebApi.GetAsync(client, uri, cancellationToken);
        using (document)
        {
            if (!available) return new(SteamProofStatus.ProviderUnavailable);
            if (document is null || !SteamWebApi.Object(document.RootElement, "response", out JsonElement response))
                return Rejected(appId, "invalid_response");
            if (response.TryGetProperty("error", out JsonElement error))
            {
                int? errorCode = error.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    error.TryGetProperty("errorcode", out JsonElement code) && code.ValueKind == System.Text.Json.JsonValueKind.Number &&
                    code.TryGetInt32(out int number) ? number : null;
                return Rejected(appId, "provider_error", errorCode);
            }
            if (!SteamWebApi.Object(response, "params", out JsonElement parameters) ||
                !string.Equals(SteamWebApi.String(parameters, "result"), "OK", StringComparison.Ordinal))
                return Rejected(appId, "unexpected_result");
            string? subject = SteamWebApi.String(parameters, "steamid");
            return SteamWebApi.IsSteamId(subject) ? new(SteamProofStatus.Verified, subject) : Rejected(appId, "invalid_steam_id");
        }
    }

    private SteamProofResult Rejected(uint appId, string reason, int? providerErrorCode = null)
    {
        logger?.LogInformation("Steam proof rejected for app {AppId}: {Reason}; provider error {ProviderErrorCode}",
            appId, reason, providerErrorCode);
        return new(SteamProofStatus.InvalidProof);
    }
}
