using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Infrastructure.StoreAuth;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.GameAuth;

public sealed class AuthAttemptStore(IGameContextStore store, GameAuthCryptography crypto,
    IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock)
{
    public async Task<AuthAttemptReply?> CreateAsync(string channel, string protocol, Guid runId, string challenge,
        Guid? contextId, uint steamAppId, CancellationToken cancellationToken)
    {
        if (channel is not (GameLaunchChannels.Steam or GameLaunchChannels.Avalon) || string.IsNullOrWhiteSpace(protocol) || protocol.Length > GameAuthPolicy.MaximumProtocolVersionCharacters ||
            runId == Guid.Empty || !GameAuthCryptography.IsToken(challenge))
        {
            return null;
        }

        if (options.Value.ResolveSteamApplication(steamAppId) is null ||
            (channel == GameLaunchChannels.Avalon && steamAppId != options.Value.SteamAppId))
        {
            return null;
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        string credential = GameAuthCryptography.NewToken();
        string identity = channel == GameLaunchChannels.Avalon ? GameAuthCryptography.NewToken() : SteamTicketIdentity.Create(options.Value, steamAppId);
        string application = channel == GameLaunchChannels.Avalon ? "avalon.base" :
            options.Value.ResolveSteamApplication(steamAppId)!.Restricted ? "steam.playtest" : "steam.main";
        var attempt = new AuthAttemptRecord
        {
            Id = Guid.NewGuid(),
            ClientRunId = runId,
            SteamAppId = steamAppId,
            ApplicationKey = application,
            ProviderChallenge = identity,
            Channel = channel,
            ProtocolVersion = protocol,
            ContextId = contextId,
            LinkChallenge = challenge,
            CreatedAt = now,
            ExpiresAt = now.Add(GameAuthPolicy.AttemptLifetime),
            ExpectedSteamIdentity = identity,
        };
        return await store.CompareExchangeAsync([new(Key(credential), null, GameAuthJson.Serialize(attempt), attempt.ExpiresAt)], cancellationToken)
            ? new(credential, identity, attempt.ExpiresAt) : null;
    }

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
