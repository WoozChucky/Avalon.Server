using System.Data.Common;
using System.Globalization;
using Avalon.Api.Hosting.Worlds;
using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Services;

/// <summary>Resumes durable pending transitions; no cross-database transaction is assumed.</summary>
public sealed class GameSessionFenceService(IGameSessionRepository sessions, GameAuthorizationService authorization,
    IWorldRepositories worlds, IAccountRepository accounts, IOptions<GameWorkloadConfiguration> workloads, TimeProvider clock,
    GameApplicationAccessPolicy applications, IWorldPlaceStore places, PlaceLedgerOutage outage,
    IOptions<WorldPlacesConfiguration> placeOptions)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task<GameSessionLeaseReply> HeartbeatAsync(string serverId, AccountId accountId, Guid sessionId, long fence, CancellationToken cancellationToken)
    {
        ContextRead read = await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken);
        if (read.Unavailable) return Unavailable;
        (GameSession Head, GameContextRecord Context, Account Root)? valid = read.Valid;
        if (valid is null || valid.Value.Head.State != GameSessionState.Active) return GameSessionLeaseReply.Failure(GameAuthErrors.SessionRevoked);
        (GameSession? head, GameContextRecord? context, Account _) = valid.Value;
        DateTime licenseUntil = Deadline(context);
        DateTime until = Min(Now.Add(GameAuthPolicy.SessionLeaseLifetime), licenseUntil);
        if (!await sessions.TryRenewAsync(accountId, sessionId, fence, serverId, head.CredentialsVersion, head.SessionEpoch,
                Now, until, licenseUntil, cancellationToken))
        {
            return GameSessionLeaseReply.Failure(GameAuthErrors.SessionRevoked);
        }

        if (await worlds.GameplayFences(new WorldId(head.WorldId)).RenewAsync(new(accountId, sessionId, fence), until, cancellationToken) is not { } guarded)
            return GameSessionLeaseReply.Failure(GameAuthErrors.BarrierPending);
        read = await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken);
        if (read.Unavailable) return Unavailable;
        if (read.Valid is not { } current || current.Head.State != GameSessionState.Active)
            return GameSessionLeaseReply.Failure(GameAuthErrors.SessionRevoked);
        GameSessionLeaseReply active = Reply(current.Head, current.Root, guarded);
        await KeepPlaceAsync(current.Head, active.LeaseUntil!.Value, false, "heartbeat", cancellationToken);
        return active;
    }
    /// <summary>
    /// The world ended the session for <paramref name="reason"/> (a <see cref="GameSessionEndReasons"/> value, or none
    /// from a world before #967), which decides whether its place is held (#967).
    /// </summary>
    public async Task<GameSessionLeaseReply> EndAsync(string serverId, AccountId accountId, Guid sessionId, long fence, string? reason,
        CancellationToken cancellationToken)
    {
        // Cleanup needs no live context: logout/recovery/ownership loss are reasons to flush and end.
        GameServerDefinition? definition = workloads.Value.Servers.SingleOrDefault(s => s.ServerId == serverId);
        GameSession? head = await sessions.FindAsync(accountId, cancellationToken);
        if (definition is null || head is null || head.ServerId != serverId || head.WorldId != definition.WorldId ||
            head.GameSessionId != sessionId || head.FencingToken != fence || sessionId == Guid.Empty || fence <= 0)
        {
            return GameSessionLeaseReply.Failure(GameAuthErrors.SessionReplaced);
        }

        var authority = new GameplayWriteAuthority(accountId, sessionId, fence);
        IGameplayFenceRepository guard = worlds.GameplayFences(new WorldId(head.WorldId));
        if (await guard.EndAsync(authority, cancellationToken))
        {
            if (await sessions.TryEndAsync(accountId, sessionId, fence, Now, cancellationToken))
                return await EndedAsync(head.WorldId, accountId, fence, reason, cancellationToken);
            return GameSessionLeaseReply.Failure(GameAuthErrors.SessionReplaced);
        }

        if (!await NeverReachedItsGuardAsync(guard, head, cancellationToken))
            return GameSessionLeaseReply.Failure(GameAuthErrors.BarrierPending);
        if (await sessions.TryExpirePendingAsync(accountId, sessionId, fence, cancellationToken))
        {
            // An activation still in flight may have reached the guard since: block it now, so it cannot activate there.
            await guard.EndAsync(authority, cancellationToken);
            return await EndedAsync(head.WorldId, accountId, fence, reason, cancellationToken);
        }

        // The head changed since it was read: replaced, or made active by an activation in flight, which ends as any
        // active session does, its guard blocked before the head ends.
        GameSession? current = await sessions.FindAsync(accountId, cancellationToken);
        if (current is null || current.GameSessionId != sessionId || current.FencingToken != fence)
            return GameSessionLeaseReply.Failure(GameAuthErrors.SessionReplaced);
        if (!await guard.EndAsync(authority, cancellationToken))
            return GameSessionLeaseReply.Failure(GameAuthErrors.BarrierPending);
        if (await sessions.TryEndAsync(accountId, sessionId, fence, Now, cancellationToken))
            return await EndedAsync(head.WorldId, accountId, fence, reason, cancellationToken);
        return GameSessionLeaseReply.Failure(GameAuthErrors.SessionReplaced);
    }

    /// <summary>
    /// The session (fencing token <paramref name="fence"/>) ended, and the answer is "ended": a drop (or no reason) holds
    /// its place for the grace, every other reason releases it (#967), unless a newer session owns it or a ticket's hold
    /// still runs.
    /// </summary>
    private async Task<GameSessionLeaseReply> EndedAsync(ushort worldId, AccountId accountId, long fence, string? reason,
        CancellationToken cancellationToken)
    {
        PlaceEnd end = GameSessionEndReasons.HoldsPlace(reason) && await MayHoldAPlaceAsync(accountId, cancellationToken)
            ? PlaceEnd.Hold
            : PlaceEnd.Remove;
        await ReleasePlaceAsync(worldId, accountId, fence, end, cancellationToken);
        // A stop or a maintenance cutoff: after it everyone is equal, so the places held for sessions that dropped before
        // it go too (decision 10). The first such end of the burst releases them; the store asks it at most once a window.
        if (reason is GameSessionEndReasons.Shutdown or GameSessionEndReasons.Maintenance)
            await ReleaseHeldPlacesAsync(worldId, cancellationToken);
        return new() { State = "ended" };
    }

    private async Task ReleaseHeldPlacesAsync(ushort worldId, CancellationToken cancellationToken)
    {
        try
        {
            await places.ReleaseHeldAsync(worldId, cancellationToken);
        }
        catch (Exception error) when (PlaceLedgerOutage.IsOutage(error))
        {
            outage.Report("held places' release", error);
        }
    }

    /// <summary>
    /// Whether the account may keep a held place: identity owns the account's standing, which the world's reason cannot
    /// always tell (#967). A ban or a deactivation whose notice the world missed closes the connection as a drop (the
    /// heartbeat is refused), and a ban between the ticket and the activation ends the pending session with no reason:
    /// an account no longer active (banned, deactivated, or being consolidated), or gone, holds nothing. A moved session
    /// epoch alone does not count: a password, email or role change moves it too, and those keep the place. When the
    /// account cannot be read, the reason decides, as before.
    /// </summary>
    private async Task<bool> MayHoldAPlaceAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        try
        {
            Account? root = await accounts.FindByIdAsync(accountId, false, cancellationToken);
            return root is { Status: AccountStatus.Active, GameplayConsolidationId: null };
        }
        catch (Exception error) when (error is DbException or TimeoutException or InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>The session owns its place to <paramref name="until"/>; an activation also spends its own ticket's hold.</summary>
    private async Task KeepPlaceAsync(GameSession head, DateTime until, bool activation, string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await places.RenewAsync(head.WorldId, head.AccountId.Value, head.FencingToken, head.GameSessionId, until, activation,
                cancellationToken);
        }
        catch (Exception error) when (PlaceLedgerOutage.IsOutage(error))
        {
            outage.Report(operation, error);
        }
    }

    private async Task ReleasePlaceAsync(ushort worldId, AccountId accountId, long fence, PlaceEnd end,
        CancellationToken cancellationToken)
    {
        try
        {
            await places.EndAsync(worldId, accountId.Value, fence, end, Now.Add(placeOptions.Value.HeldPlace), cancellationToken);
        }
        catch (Exception error) when (PlaceLedgerOutage.IsOutage(error))
        {
            outage.Report("session end", error);
        }
    }

    /// <summary>
    /// Whether a pending session's activation failed before its target barrier (#1013): the guard still holds an older
    /// session's token, or none, so it grants this session nothing and there is nothing to block. An active session's
    /// guard always holds its token, since activation advances the guard before the session activates.
    /// </summary>
    private static async Task<bool> NeverReachedItsGuardAsync(IGameplayFenceRepository guard, GameSession head, CancellationToken cancellationToken)
    {
        if (head.State != GameSessionState.Pending) return false;
        AccountGameplayFence? held = await guard.FindAsync(head.AccountId, cancellationToken);
        return held is null || held.FencingToken < head.FencingToken;
    }
    public async Task<GameSessionLeaseReply> ActivateAsync(string serverId, AccountId accountId, Guid sessionId, long fence, CancellationToken cancellationToken)
    {
        ContextRead read = await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken);
        if (read.Unavailable) return Unavailable;
        (GameSession Head, GameContextRecord Context, Account Root)? valid = read.Valid;
        if (valid is null) return GameSessionLeaseReply.Failure(GameAuthErrors.SessionRevoked);
        (GameSession? head, GameContextRecord? context, Account? root) = valid.Value;
        var authority = new GameplayWriteAuthority(accountId, sessionId, fence);
        DateTime until = Min(Now.Add(GameAuthPolicy.SessionLeaseLifetime), head.LicenseUntil, Deadline(context));
        if (head.State == GameSessionState.Pending)
        {
            if (head.PreviousWorldId is { } previous && previous != head.WorldId &&
                await worlds.GameplayFences(new WorldId(previous)).AdvanceAsync(authority, true, until, cancellationToken) is null)
            {
                return GameSessionLeaseReply.Failure(GameAuthErrors.BarrierPending);
            }

            // The target guard's own clock caps its end (#1009): the session activates with that end, never a later one.
            if (await worlds.GameplayFences(new WorldId(head.WorldId)).AdvanceAsync(authority, false, until, cancellationToken) is not { } held)
                return GameSessionLeaseReply.Failure(GameAuthErrors.BarrierPending);
            // An epoch/recovery/ban change during either world barrier must not activate SQL authority.
            read = await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken);
            if (read.Unavailable) return Unavailable;
            if (read.Valid is null)
                return GameSessionLeaseReply.Failure(GameAuthErrors.SessionRevoked);
            if (!await sessions.TryActivateAsync(accountId, sessionId, fence, Now, held, cancellationToken))
                return GameSessionLeaseReply.Failure(GameAuthErrors.SessionRevoked);
        }
        // Recover a crash after SQL activation by completing its same target guard, never a new reservation.
        read = await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken);
        if (read.Unavailable) return Unavailable;
        valid = read.Valid;
        if (valid is null || valid.Value.Head.State != GameSessionState.Active) return GameSessionLeaseReply.Failure(GameAuthErrors.SessionRevoked);
        (head, context, root) = valid.Value;
        if (await worlds.GameplayFences(new WorldId(head.WorldId)).ActivateAsync(authority, head.LeaseUntil, cancellationToken) is not { } guarded)
            return GameSessionLeaseReply.Failure(GameAuthErrors.BarrierPending);
        read = await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken);
        if (read.Unavailable) return Unavailable;
        if (read.Valid is not { } current || current.Head.State != GameSessionState.Active)
            return GameSessionLeaseReply.Failure(GameAuthErrors.SessionRevoked);
        GameSessionLeaseReply active = Reply(current.Head, current.Root, guarded);
        // A session is one per account: its place in any other world goes (#967).
        await KeepPlaceAsync(current.Head, active.LeaseUntil!.Value, true, "activation", cancellationToken);
        return active;
    }
    /// <summary>A database outage while checking the session's context: the world keeps its lease and asks again.</summary>
    private static GameSessionLeaseReply Unavailable => GameSessionLeaseReply.Failure(GameAuthErrors.ServiceUnavailable);

    /// <summary>The session, its context and account when all still stand; <see cref="Unavailable"/> on a database outage.</summary>
    private readonly record struct ContextRead((GameSession Head, GameContextRecord Context, Account Root)? Valid, bool Unavailable);

    private async Task<ContextRead> ReadAsync(string serverId, AccountId accountId,
        Guid sessionId, long fence, CancellationToken cancellationToken)
    {
        GameServerDefinition? definition = workloads.Value.Servers.SingleOrDefault(s => s.ServerId == serverId);
        if (definition is null || accountId.Value <= 0 || sessionId == Guid.Empty || fence <= 0) return default;
        GameSession? head = await sessions.FindAsync(accountId, cancellationToken);
        if (head is null || head.GameSessionId != sessionId || head.FencingToken != fence || head.ServerId != serverId ||
            head.WorldId != definition.WorldId || head.State == GameSessionState.Ended || head.LeaseUntil <= Now || head.LicenseUntil <= Now)
        {
            return default;
        }

        // A license or account that could not be read is an outage, not a revocation: the player is not kicked (#862).
        GameContextStanding standing = await authorization.GetContextStandingByIdAsync(head.GameContextId, true, cancellationToken);
        if (standing.Unavailable) return new(null, true);
        GameContextRecord? context = standing.Context;
        if (context is null || !applications.AllowsWorld(context.ApplicationKey, head.WorldId) || context.AccountId != accountId.Value || context.Environment != head.Environment ||
            context.CredentialsVersion != head.CredentialsVersion || context.SessionEpoch != head.SessionEpoch || Deadline(context) <= Now)
        {
            return default;
        }

        Account? root = await accounts.FindByIdAsync(accountId, false, cancellationToken);
        // The password lock is not asked (#882): it guards the password steps only, so a guesser cannot end a session.
        if (root is null || root.Status != AccountStatus.Active || root.GameplayConsolidationId is not null ||
            (root.AccessLevel & AccountAccessLevel.Player) == 0 || root.CredentialsVersion != head.CredentialsVersion || root.SessionEpoch != head.SessionEpoch)
        {
            return default;
        }

        return new((head, context, root), false);
    }
    private static DateTime Deadline(GameContextRecord context) => GameContextAuthorizationWindow.Deadline(context)!.Value;
    private static DateTime Min(params DateTime[] values) => values.Min();
    /// <summary>
    /// The active reply. Its lease ends at the earlier of the session's end and the end the target guard holds: the
    /// guard's end is capped on the characters database's clock, so the world is never given a lease that outlasts the
    /// guard its final save needs (#1009).
    /// </summary>
    private static GameSessionLeaseReply Reply(GameSession head, Account root, DateTime guarded) => new()
    {
        State = GameAuthStates.Active,
        AccountId = head.AccountId.Value.ToString(CultureInfo.InvariantCulture),
        GameSessionId = head.GameSessionId.ToString("D"),
        GameContextId = head.GameContextId.ToString("D"),
        FencingToken = head.FencingToken.ToString(CultureInfo.InvariantCulture),
        ServerId = head.ServerId,
        WorldId = head.WorldId,
        AccessLevel = (ushort)root.AccessLevel,
        CredentialsVersion = head.CredentialsVersion,
        SessionEpoch = head.SessionEpoch.ToString(CultureInfo.InvariantCulture),
        LeaseUntil = Min(head.LeaseUntil, guarded),
        AuthorizationUntil = head.LicenseUntil,
    };
}
