using System.Globalization;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.Extensions.Options;

namespace Avalon.Infrastructure.GameAuth;

/// <summary>Atomic Redis claims plus durable SQL reservation recovery, with no cross-store transaction assumption.</summary>
public sealed class JoinTicketStore(IGameContextStore store, GameAuthCryptography crypto,
    GameAuthorizationService authorization, IGameSessionRepository sessions, IGameServerAllocator allocator,
    IOptions<StoreAuthenticationConfiguration> options, TimeProvider clock, GameApplicationAccessPolicy applications,
    IWorldPlaceStore places, PlaceLedgerOutage outage)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private string Key(string kind, string id) => CacheKeys.GameAuth(options.Value.Environment, kind, id);

    public async Task<GameJoinReply> IssueAsync(string credential, ushort worldId, uint? characterId,
        Guid requestId, bool confirmTakeover, bool reconnect, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty || worldId == 0 || characterId == 0) return new(GameAuthErrors.InvalidRequest);
        GameContextStanding standing = await authorization.GetContextStandingAsync(credential, true, cancellationToken);
        if (standing.AccountRefusal is { } refusal) return new(refusal);
        GameContextRecord? context = standing.Context;
        if (context?.AccountId is not { } accountId) return new(GameAuthErrors.AuthorizationRequired);
        // Permanent, so not WorldUnavailable, which means "try later" (#1053): the allocator answers it the same way.
        if (!applications.AllowsWorld(context.ApplicationKey, worldId)) return new(GameAuthErrors.WorldNotAllowed);
        string issueKey = Key("join-issue", context.Id.ToString("N") + ":" + requestId.ToString("N"));
        string binding = crypto.Binding("join-issue", requestId, GameAuthCryptography.Digest($"{context.Generation}:{worldId}:{characterId}:{confirmTakeover}:{reconnect}"));
        JoinIssueReceipt? prior = GameAuthJson.Deserialize<JoinIssueReceipt>(await store.ReadAsync(issueKey, cancellationToken));
        if (prior is not null)
        {
            if (prior.Binding != binding) return new(GameAuthErrors.IdempotencyConflict);
            GameJoinReply? replay = GameAuthJson.Deserialize<GameJoinReply>(crypto.UnprotectText(prior.Envelope, issueKey + ":" + binding));
            return replay?.ExpiresAt > Now ? replay : new(GameAuthErrors.TicketExpired);
        }
        GameWorldAllocation allocation = await allocator.FindAsync(context, worldId, characterId, cancellationToken);
        // Why, so a client retries only what a retry may change (#1053); a client too old is told the version it needs.
        if (allocation.Destination is not { } destination) return new(allocation.IssueError, RequiredVersion: allocation.RequiredVersion);
        GameSession? head = await sessions.FindAsync(new AccountId(accountId), cancellationToken);
        if (reconnect && (head is null || head.GameContextId != context.Id || head.WorldId != worldId)) return new(GameAuthErrors.ReconnectUnavailable);
        // A confirmed takeover (a join) replaces a live session, pending or active (#1013); a reconnect only an active
        // one, whatever it says about a takeover.
        if (head?.LeaseUntil > Now && head.State != GameSessionState.Ended &&
            (head.State == GameSessionState.Pending ? reconnect || !confirmTakeover : !confirmTakeover && !reconnect))
        {
            return new(GameAuthErrors.ActiveGameSession);
        }

        if (head?.FencingToken == long.MaxValue) return new(GameAuthErrors.AccountUnavailable);
        DateTime until = GameContextAuthorizationWindow.Deadline(context)!.Value;
        DateTime expires = Min(Now.Add(GameAuthPolicy.JoinTicketLifetime), context.CredentialExpiresAt, until);
        if (expires <= Now) return new(GameAuthErrors.AuthorizationRequired);
        // Capacity last (#967): every check above stands as it was, and a refusal above takes no place. The place lasts
        // 15 s past the ticket, for a redemption in flight. Part 1 keeps the ledger and enforces nothing: no limit is asked.
        DateTime placeUntil = expires.Add(GameAuthPolicy.TicketPlaceMargin);
        PlaceDecision place = await places.EnterAsync(new PlaceRequest(worldId, accountId, placeUntil,
            Limit: null, Staff: false, QueueCapacity: 0), cancellationToken);
        // A place this ticket took, and only one it took, is given back if the ticket is not issued after all.
        bool took = place is { Outcome: PlaceOutcome.Admitted, Source: PlaceSource.Taken };
        string ticket = GameAuthCryptography.NewToken();
        string ticketKey = Key("join-ticket", GameAuthCryptography.Digest(ticket));
        var grant = new JoinTicketGrant
        {
            TicketId = Guid.NewGuid(),
            GameSessionId = Guid.NewGuid(),
            ContextId = context.Id,
            ContextGeneration = context.Generation,
            AccountId = accountId,
            CredentialsVersion = context.CredentialsVersion,
            SessionEpoch = context.SessionEpoch,
            ServerId = destination.ServerId,
            WorldId = worldId,
            CharacterId = characterId,
            Environment = options.Value.Environment,
            ExpectedFence = head?.FencingToken ?? 0,
            Takeover = confirmTakeover || reconnect,
            Reconnect = reconnect,
            ExpiresAt = expires,
            AuthorizationUntil = until,
        };
        var reply = new GameJoinReply(JoinTicket: ticket, ExpiresAt: expires, Destination: destination);
        string contextKey = Key("context", context.Id.ToString("N"));
        bool issued;
        try
        {
            issued = await store.CompareExchangeAsync([
                new(contextKey, GameAuthJson.Serialize(context), GameAuthJson.Serialize(context), context.AbsoluteExpiresAt),
                new(issueKey, null, GameAuthJson.Serialize(new JoinIssueReceipt(binding, crypto.ProtectText(GameAuthJson.Serialize(reply), issueKey + ":" + binding))), expires),
                new(ticketKey, null, GameAuthJson.Serialize(grant), expires.Add(GameAuthPolicy.JoinReceiptRetention)),
            ], cancellationToken);
        }
        catch when (took)
        {
            await WithdrawPlaceAsync(worldId, accountId, placeUntil);
            throw;
        }

        if (issued) return reply;
        if (took) await WithdrawPlaceAsync(worldId, accountId, placeUntil);
        return new(GameAuthErrors.ContextChanged);
    }

    /// <summary>
    /// Gives back the place a ticket took when the ticket was not issued (#967), best effort and whatever the request's
    /// cancellation: repeated failed issues must not hold places until they lapse. Only while that ticket is still what
    /// holds the place (<see cref="PlaceEnd.Withdraw"/>).
    /// </summary>
    private async Task WithdrawPlaceAsync(ushort worldId, long accountId, DateTime placeUntil)
    {
        try
        {
            await places.EndAsync(worldId, accountId, PlaceEnd.Withdraw, placeUntil, CancellationToken.None);
        }
        catch (Exception error) when (PlaceLedgerOutage.IsOutage(error))
        {
            outage.Report("withdrawal", error);
        }
    }

    public async Task<JoinRedemptionReceipt> RedeemAsync(string ticket, string serverId, Guid connectionId,
        Guid redemptionId, CancellationToken cancellationToken)
    {
        if (!GameAuthCryptography.IsToken(ticket) || connectionId == Guid.Empty || redemptionId == Guid.Empty)
            return JoinRedemptionReceipt.Failure(GameAuthErrors.InvalidTicket);
        string key = Key("join-ticket", GameAuthCryptography.Digest(ticket));
        string binding = crypto.Binding("join-redeem", redemptionId, GameAuthCryptography.Digest($"{serverId}:{connectionId:N}"));
        for (int retry = 0; retry < GameAuthPolicy.MutationAttempts; retry++)
        {
            string? raw = await store.ReadAsync(key, cancellationToken);
            JoinTicketGrant? grant = GameAuthJson.Deserialize<JoinTicketGrant>(raw);
            if (grant is null || grant.ServerId != serverId || grant.Environment != options.Value.Environment ||
                (grant.Binding is not null && grant.Binding != binding))
            {
                return JoinRedemptionReceipt.Failure(GameAuthErrors.InvalidTicket);
            }

            GameContextRecord? context = await authorization.GetContextByIdAsync(grant.ContextId, true, cancellationToken);
            if (context is null || context.AccountId != grant.AccountId || context.CredentialsVersion != grant.CredentialsVersion ||
                context.SessionEpoch != grant.SessionEpoch)
            {
                return JoinRedemptionReceipt.Failure(GameAuthErrors.ContextRevoked);
            }

            // Permanent for this context, so not WorldUnavailable, which the world answers its client as "try later" (#1050).
            if (!applications.AllowsWorld(context.ApplicationKey, grant.WorldId)) return JoinRedemptionReceipt.Failure(GameAuthErrors.WorldNotAllowed);
            if (grant.Receipt is not null)
            {
                if (grant.ReceiptExpiresAt <= Now) return JoinRedemptionReceipt.Failure(GameAuthErrors.TicketExpired);
                JoinRedemptionReceipt? receipt = GameAuthJson.Deserialize<JoinRedemptionReceipt>(crypto.UnprotectText(grant.Receipt, key + ":" + binding));
                if (receipt is null) return JoinRedemptionReceipt.Failure(GameAuthErrors.InvalidTicket);
                if (receipt.Error is not null) return receipt;
                GameSession? current = await sessions.FindAsync(new AccountId(grant.AccountId), cancellationToken);
                return current is not null && current.GameSessionId == grant.GameSessionId && current.ServerId == serverId &&
                    current.FencingToken.ToString(CultureInfo.InvariantCulture) == receipt.FencingToken && current.LeaseUntil > Now &&
                    current.State != GameSessionState.Ended ? receipt : JoinRedemptionReceipt.Failure(GameAuthErrors.SessionReplaced);
            }
            if (grant.ExpiresAt <= Now || grant.AuthorizationUntil <= Now || context.Generation != grant.ContextGeneration)
                return JoinRedemptionReceipt.Failure(GameAuthErrors.TicketExpired);
            if (grant.WorkerUntil > Now) return JoinRedemptionReceipt.Failure(GameAuthErrors.InProgress);
            GameWorldAllocation allocation = await allocator.FindAsync(context, grant.WorldId, grant.CharacterId, cancellationToken);
            // The allocator's reason, which the world maps onto its client's refusal (#1053); a world reassigned to
            // another workload since the issue is "try later".
            if (allocation.Destination is not { } destination) return JoinRedemptionReceipt.Failure(allocation.RedemptionError);
            if (destination.ServerId != serverId) return JoinRedemptionReceipt.Failure(GameAuthErrors.WorldUnavailable);
            JoinTicketGrant claimed = grant with { Binding = binding, WorkerUntil = Min(Now.Add(GameAuthPolicy.MutationClaimLifetime), grant.ExpiresAt) };
            string claimedRaw = GameAuthJson.Serialize(claimed);
            if (!await store.CompareExchangeAsync([new(key, raw, claimedRaw, grant.ExpiresAt.Add(GameAuthPolicy.JoinReceiptRetention))], cancellationToken)) continue;
            var reservation = new GameSessionReservation(new AccountId(grant.AccountId), grant.ExpectedFence, grant.GameSessionId,
                serverId, grant.WorldId, grant.Environment, grant.CredentialsVersion, grant.SessionEpoch, grant.AuthorizationUntil, grant.Takeover)
            { GameContextId = grant.ContextId, AdmissionExpiresAt = grant.ExpiresAt, Reconnect = grant.Reconnect };
            GameSession? head = await sessions.TryReserveAsync(reservation, Now, cancellationToken);
            if (head is not null) await KeepPlaceAsync(grant.WorldId, grant.AccountId, head.LeaseUntil, cancellationToken);
            JoinRedemptionReceipt reply = head is null ? JoinRedemptionReceipt.Failure(GameAuthErrors.SessionConflict) :
                grant.ExpiresAt <= Now ? JoinRedemptionReceipt.Failure(GameAuthErrors.TicketExpired) : new JoinRedemptionReceipt
                {
                    AccountId = grant.AccountId.ToString(CultureInfo.InvariantCulture),
                    GameSessionId = head.GameSessionId.ToString("D"),
                    GameContextId = grant.ContextId.ToString("D"),
                    FencingToken = head.FencingToken.ToString(CultureInfo.InvariantCulture),
                    ConnectionId = connectionId.ToString("D"),
                    RedemptionId = redemptionId.ToString("D"),
                    ServerId = serverId,
                    WorldId = grant.WorldId,
                    CharacterId = grant.CharacterId,
                    CredentialsVersion = head.CredentialsVersion,
                    SessionEpoch = head.SessionEpoch.ToString(CultureInfo.InvariantCulture),
                    LeaseUntil = head.LeaseUntil,
                    AuthorizationUntil = head.LicenseUntil,
                };
            DateTime expiry = Min(grant.ExpiresAt.Add(GameAuthPolicy.JoinReceiptRetention), grant.AuthorizationUntil, head?.LeaseUntil ?? grant.ExpiresAt);
            JoinTicketGrant finished = claimed with { Receipt = crypto.ProtectText(GameAuthJson.Serialize(reply), key + ":" + binding), ReceiptExpiresAt = expiry, WorkerUntil = null };
            return await store.CompareExchangeAsync([new(key, claimedRaw, GameAuthJson.Serialize(finished), expiry)], cancellationToken)
                ? reply : JoinRedemptionReceipt.Failure(GameAuthErrors.InProgress);
        }
        return JoinRedemptionReceipt.Failure(GameAuthErrors.InProgress);
    }

    /// <summary>Renews the account's place to the pending session's lease; Redis down changes nothing for the session (#967).</summary>
    private async Task KeepPlaceAsync(ushort worldId, long accountId, DateTime until, CancellationToken cancellationToken)
    {
        try
        {
            await places.RenewAsync(worldId, accountId, until, leaveOtherWorlds: false, cancellationToken);
        }
        catch (Exception error) when (PlaceLedgerOutage.IsOutage(error))
        {
            outage.Report("redemption", error);
        }
    }

    private static DateTime Min(params DateTime[] values) => values.Min();
}
