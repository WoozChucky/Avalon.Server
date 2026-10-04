using System.Security.Cryptography;
using Avalon.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.GameAuth;

public sealed class AuthAttemptStore(IGameContextStore store, GameAuthCryptography crypto,
    IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock)
{
    public async Task<AuthAttemptReply?> CreateAsync(string channel, string protocol, Guid runId, string challenge,
        Guid? contextId, CancellationToken cancellationToken)
    {
        if (channel is not ("steam" or "avalon") || string.IsNullOrWhiteSpace(protocol) || protocol.Length > 32 ||
            runId == Guid.Empty || !GameAuthCryptography.IsToken(challenge)) return null;
        var now = clock.GetUtcNow().UtcDateTime;
        var credential = GameAuthCryptography.NewToken();
        var identity = options.Value.SteamIdentityPrefix + ":" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var attempt = new AuthAttemptRecord
        {
            Id = Guid.NewGuid(), ClientRunId = runId, Channel = channel, ProtocolVersion = protocol, ContextId = contextId,
            LinkChallenge = challenge, CreatedAt = now, ExpiresAt = now.AddSeconds(120), ExpectedSteamIdentity = identity,
        };
        return await store.CompareExchangeAsync([new(Key(credential), null, GameAuthJson.Serialize(attempt), attempt.ExpiresAt)], cancellationToken)
            ? new(credential, identity, attempt.ExpiresAt) : null;
    }

    public string Key(string credential) => CacheKeys.GameAuth(options.Value.Environment, "attempt", GameAuthCryptography.Digest(credential));
    public string ReceiptBinding(string key, string requestBinding) => key + ":" + requestBinding;
    public GameAuthReply? Receipt(AuthAttemptRecord record, string key, string binding) =>
        record.Binding == binding && record.Receipt is not null && record.ReceiptExpiresAt > clock.GetUtcNow().UtcDateTime
            ? crypto.Unprotect(record.Receipt, ReceiptBinding(key, binding)) : null;
}
