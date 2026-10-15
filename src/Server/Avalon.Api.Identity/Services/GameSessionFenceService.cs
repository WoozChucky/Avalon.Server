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
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Services;

/// <summary>Resumes durable pending transitions; no cross-database transaction is assumed.</summary>
public sealed class GameSessionFenceService(IGameSessionRepository sessions, GameAuthorizationService authorization,
    IWorldRepositories worlds, IAccountRepository accounts, IOptions<GameWorkloadConfiguration> workloads, TimeProvider clock,
    GameApplicationAccessPolicy applications)
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
        valid = read.Valid;
        return valid is { } current && current.Head.State == GameSessionState.Active
            ? Reply(current.Head, current.Root, guarded) : GameSessionLeaseReply.Failure(GameAuthErrors.SessionRevoked);
    }
    public async Task<GameSessionLeaseReply> EndAsync(string serverId, AccountId accountId, Guid sessionId, long fence, CancellationToken cancellationToken)
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
            return await sessions.TryEndAsync(accountId, sessionId, fence, Now, cancellationToken)
                ? new() { State = "ended" } : GameSessionLeaseReply.Failure(GameAuthErrors.SessionReplaced);
        }

        if (!await NeverReachedItsGuardAsync(guard, head, cancellationToken))
            return GameSessionLeaseReply.Failure(GameAuthErrors.BarrierPending);
        if (!await sessions.TryEndAsync(accountId, sessionId, fence, Now, cancellationToken))
            return GameSessionLeaseReply.Failure(GameAuthErrors.SessionReplaced);
        // An activation still in flight may have reached the guard since: block it now, so it cannot activate there.
        await guard.EndAsync(authority, cancellationToken);
        return new() { State = "ended" };
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
        valid = read.Valid;
        return valid is { } current && current.Head.State == GameSessionState.Active
            ? Reply(current.Head, current.Root, guarded) : GameSessionLeaseReply.Failure(GameAuthErrors.SessionRevoked);
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
