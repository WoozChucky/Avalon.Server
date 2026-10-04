using System.Globalization;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.GameAuth;

/// <summary>Atomic Redis claims plus durable SQL reservation recovery, with no cross-store transaction assumption.</summary>
public sealed class JoinTicketStore(IGameContextStore store, GameAuthCryptography crypto,
    GameAuthorizationService authorization, IGameSessionRepository sessions, IGameServerAllocator allocator,
    IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private string Key(string kind, string id) => CacheKeys.GameAuth(options.Value.Environment, kind, id);

    public async Task<GameJoinReply> IssueAsync(string credential, ushort worldId, uint? characterId,
        Guid requestId, bool confirmTakeover, bool reconnect, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty || worldId == 0 || characterId == 0) return new("INVALID_REQUEST");
        var context = await authorization.GetContextAsync(credential, true, cancellationToken);
        if (context?.AccountId is not { } accountId) return new("AUTHORIZATION_REQUIRED");
        var issueKey = Key("join-issue", context.Id.ToString("N") + ":" + requestId.ToString("N"));
        var binding = crypto.Binding("join-issue", requestId, GameAuthCryptography.Digest($"{context.Generation}:{worldId}:{characterId}:{confirmTakeover}:{reconnect}"));
        var prior = GameAuthJson.Deserialize<JoinIssueReceipt>(await store.ReadAsync(issueKey, cancellationToken));
        if (prior is not null)
        {
            if (prior.Binding != binding) return new("IDEMPOTENCY_CONFLICT");
            var replay = GameAuthJson.Deserialize<GameJoinReply>(crypto.UnprotectText(prior.Envelope, issueKey + ":" + binding));
            return replay?.ExpiresAt > Now ? replay : new("TICKET_EXPIRED");
        }
        var destination = await allocator.FindAsync(context, worldId, characterId, cancellationToken);
        if (destination is null) return new("WORLD_UNAVAILABLE");
        var head = await sessions.FindAsync(new AccountId(accountId), cancellationToken);
        if (reconnect && (head is null || head.GameContextId != context.Id || head.WorldId != worldId)) return new("RECONNECT_UNAVAILABLE");
        if (head?.LeaseUntil > Now && head.State != GameSessionState.Ended &&
            (head.State == GameSessionState.Pending || (!confirmTakeover && !reconnect))) return new("ACTIVE_GAME_SESSION");
        if (head?.FencingToken == long.MaxValue) return new("ACCOUNT_UNAVAILABLE");
        var until = Min(context.AuthorizationValidUntil!.Value, context.AbsoluteExpiresAt, context.IdentityVerifiedAt!.Value.AddMinutes(30));
        var expires = Min(Now.AddSeconds(30), context.CredentialExpiresAt, until);
        if (expires <= Now) return new("AUTHORIZATION_REQUIRED");
        var ticket = GameAuthCryptography.NewToken();
        var ticketKey = Key("join-ticket", GameAuthCryptography.Digest(ticket));
        var grant = new JoinTicketGrant
        {
            TicketId = Guid.NewGuid(), GameSessionId = Guid.NewGuid(), ContextId = context.Id, ContextGeneration = context.Generation,
            AccountId = accountId, CredentialsVersion = context.CredentialsVersion, SessionEpoch = context.SessionEpoch,
            ServerId = destination.ServerId, WorldId = worldId, CharacterId = characterId, Environment = options.Value.Environment,
            ExpectedFence = head?.FencingToken ?? 0, Takeover = confirmTakeover || reconnect, ExpiresAt = expires, AuthorizationUntil = until,
        };
        var reply = new GameJoinReply(JoinTicket: ticket, ExpiresAt: expires, Destination: destination);
        var contextKey = Key("context", context.Id.ToString("N"));
        return await store.CompareExchangeAsync([
            new(contextKey, GameAuthJson.Serialize(context), GameAuthJson.Serialize(context), context.AbsoluteExpiresAt),
            new(issueKey, null, GameAuthJson.Serialize(new JoinIssueReceipt(binding, crypto.ProtectText(GameAuthJson.Serialize(reply), issueKey + ":" + binding))), expires),
            new(ticketKey, null, GameAuthJson.Serialize(grant), expires.AddSeconds(45)),
        ], cancellationToken) ? reply : new("CONTEXT_CHANGED");
    }

    public async Task<JoinRedemptionReceipt> RedeemAsync(string ticket, string serverId, Guid connectionId,
        Guid redemptionId, CancellationToken cancellationToken)
    {
        if (!GameAuthCryptography.IsToken(ticket) || connectionId == Guid.Empty || redemptionId == Guid.Empty)
            return JoinRedemptionReceipt.Failure("INVALID_TICKET");
        var key = Key("join-ticket", GameAuthCryptography.Digest(ticket));
        var binding = crypto.Binding("join-redeem", redemptionId, GameAuthCryptography.Digest($"{serverId}:{connectionId:N}"));
        for (var retry = 0; retry < 3; retry++)
        {
            var raw = await store.ReadAsync(key, cancellationToken);
            var grant = GameAuthJson.Deserialize<JoinTicketGrant>(raw);
            if (grant is null || grant.ServerId != serverId || grant.Environment != options.Value.Environment ||
                (grant.Binding is not null && grant.Binding != binding)) return JoinRedemptionReceipt.Failure("INVALID_TICKET");
            var context = await authorization.GetContextByIdAsync(grant.ContextId, true, cancellationToken);
            if (context is null || context.AccountId != grant.AccountId || context.CredentialsVersion != grant.CredentialsVersion ||
                context.SessionEpoch != grant.SessionEpoch) return JoinRedemptionReceipt.Failure("CONTEXT_REVOKED");
            if (grant.Receipt is not null)
            {
                if (grant.ReceiptExpiresAt <= Now) return JoinRedemptionReceipt.Failure("TICKET_EXPIRED");
                var receipt = GameAuthJson.Deserialize<JoinRedemptionReceipt>(crypto.UnprotectText(grant.Receipt, key + ":" + binding));
                if (receipt is null) return JoinRedemptionReceipt.Failure("INVALID_TICKET");
                if (receipt.Error is not null) return receipt;
                var current = await sessions.FindAsync(new AccountId(grant.AccountId), cancellationToken);
                return current is not null && current.GameSessionId == grant.GameSessionId && current.ServerId == serverId &&
                    current.FencingToken.ToString(CultureInfo.InvariantCulture) == receipt.FencingToken && current.LeaseUntil > Now &&
                    current.State != GameSessionState.Ended ? receipt : JoinRedemptionReceipt.Failure("SESSION_REPLACED");
            }
            if (grant.ExpiresAt <= Now || grant.AuthorizationUntil <= Now || context.Generation != grant.ContextGeneration)
                return JoinRedemptionReceipt.Failure("TICKET_EXPIRED");
            if (grant.WorkerUntil > Now) return JoinRedemptionReceipt.Failure("IN_PROGRESS");
            var destination = await allocator.FindAsync(context, grant.WorldId, grant.CharacterId, cancellationToken);
            if (destination?.ServerId != serverId) return JoinRedemptionReceipt.Failure("WORLD_UNAVAILABLE");
            var claimed = grant with { Binding = binding, WorkerUntil = Min(Now.AddSeconds(15), grant.ExpiresAt) };
            var claimedRaw = GameAuthJson.Serialize(claimed);
            if (!await store.CompareExchangeAsync([new(key, raw, claimedRaw, grant.ExpiresAt.AddSeconds(45))], cancellationToken)) continue;
            var reservation = new GameSessionReservation(new AccountId(grant.AccountId), grant.ExpectedFence, grant.GameSessionId,
                serverId, grant.WorldId, grant.Environment, grant.CredentialsVersion, grant.SessionEpoch, grant.AuthorizationUntil, grant.Takeover)
                { GameContextId = grant.ContextId, AdmissionExpiresAt = grant.ExpiresAt };
            var head = await sessions.TryReserveAsync(reservation, Now, cancellationToken);
            var reply = head is null ? JoinRedemptionReceipt.Failure("SESSION_CONFLICT") :
                grant.ExpiresAt <= Now ? JoinRedemptionReceipt.Failure("TICKET_EXPIRED") : new JoinRedemptionReceipt
                {
                    AccountId = grant.AccountId.ToString(CultureInfo.InvariantCulture), GameSessionId = head.GameSessionId.ToString("D"),
                    GameContextId = grant.ContextId.ToString("D"), FencingToken = head.FencingToken.ToString(CultureInfo.InvariantCulture),
                    ConnectionId = connectionId.ToString("D"), RedemptionId = redemptionId.ToString("D"), ServerId = serverId,
                    WorldId = grant.WorldId, CharacterId = grant.CharacterId, CredentialsVersion = head.CredentialsVersion,
                    SessionEpoch = head.SessionEpoch.ToString(CultureInfo.InvariantCulture), LeaseUntil = head.LeaseUntil, AuthorizationUntil = head.LicenseUntil,
                };
            var expiry = Min(grant.ExpiresAt.AddSeconds(45), grant.AuthorizationUntil, head?.LeaseUntil ?? grant.ExpiresAt);
            var finished = claimed with { Receipt = crypto.ProtectText(GameAuthJson.Serialize(reply), key + ":" + binding), ReceiptExpiresAt = expiry, WorkerUntil = null };
            return await store.CompareExchangeAsync([new(key, claimedRaw, GameAuthJson.Serialize(finished), expiry)], cancellationToken)
                ? reply : JoinRedemptionReceipt.Failure("IN_PROGRESS");
        }
        return JoinRedemptionReceipt.Failure("IN_PROGRESS");
    }
    private static DateTime Min(params DateTime[] values) => values.Min();
}
