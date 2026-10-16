using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.GameAuth;

public sealed class AuthAttemptStore(IGameContextStore store, GameAuthCryptography crypto,
    IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock)
{
    public string Key(string credential) => CacheKeys.GameAuth(options.Value.Environment, "attempt", GameAuthCryptography.Digest(credential));
    public async Task<AuthAttemptReply?> CreateAsync(GameApplicationSelection application, string protocol, Guid runId, string challenge,
        Guid? contextId, uint legacySteamAppId, string providerChallenge, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(protocol) || protocol.Length > GameAuthPolicy.MaximumProtocolVersionCharacters ||
            runId == Guid.Empty || !GameAuthCryptography.IsToken(challenge))
        {
            return null;
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        string credential = GameAuthCryptography.NewToken();
        var attempt = new AuthAttemptRecord
        {
            Id = Guid.NewGuid(),
            ClientRunId = runId,
            SteamAppId = legacySteamAppId,
            ApplicationKey = application.Key,
            Channel = application.Provider,
            ProtocolVersion = protocol,
            ContextId = contextId,
            LinkChallenge = challenge,
            CreatedAt = now,
            ExpiresAt = now.Add(GameAuthPolicy.AttemptLifetime),
            ExpectedSteamIdentity = providerChallenge,
            ProviderChallenge = providerChallenge,
        };
        return await store.CompareExchangeAsync([new(Key(credential), null, GameAuthJson.Serialize(attempt), attempt.ExpiresAt)], ct)
            ? new(credential, providerChallenge, attempt.ExpiresAt) : null;
    }
    public string ReceiptBinding(string key, string requestBinding) => key + ":" + requestBinding;
    public GameAuthReply? Receipt(AuthAttemptRecord record, string key, string binding) =>
        record.Binding == binding && record.Receipt is not null && record.ReceiptExpiresAt > clock.GetUtcNow().UtcDateTime
            ? crypto.Unprotect(record.Receipt, ReceiptBinding(key, binding)) : null;
}
