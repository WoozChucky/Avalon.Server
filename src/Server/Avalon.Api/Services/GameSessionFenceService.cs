using System.Globalization;
using Avalon.Api.Worlds;
using Avalon.Common.Accounts;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Services;

public sealed record GameSessionLeaseReply
{
    public string State { get; init; } = "pending";
    public string? Error { get; init; }
    public string? AccountId { get; init; }
    public string? GameSessionId { get; init; }
    public string? GameContextId { get; init; }
    public string? FencingToken { get; init; }
    public string? ServerId { get; init; }
    public ushort? WorldId { get; init; }
    public ushort? AccessLevel { get; init; }
    public int? CredentialsVersion { get; init; }
    public string? SessionEpoch { get; init; }
    public DateTime? LeaseUntil { get; init; }
    public DateTime? AuthorizationUntil { get; init; }
    public static GameSessionLeaseReply Failure(string error) => new() { Error = error };
}

/// <summary>Resumes durable pending transitions; no cross-database transaction is assumed.</summary>
public sealed class GameSessionFenceService(IGameSessionRepository sessions, GameAuthorizationService authorization,
    IWorldRepositories worlds, IAccountRepository accounts, IOptions<GameWorkloadConfiguration> workloads, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public async Task<GameSessionLeaseReply> HeartbeatAsync(string serverId, AccountId accountId, Guid sessionId, long fence, CancellationToken cancellationToken)
    {
        var valid = await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken);
        if (valid is null || valid.Value.Head.State != GameSessionState.Active) return GameSessionLeaseReply.Failure("SESSION_REVOKED");
        var (head, context, _) = valid.Value;
        var licenseUntil = Deadline(context);
        var until = Min(Now.AddSeconds(45), licenseUntil);
        if (!await sessions.TryRenewAsync(accountId, sessionId, fence, serverId, head.CredentialsVersion, head.SessionEpoch,
                Now, until, licenseUntil, cancellationToken)) return GameSessionLeaseReply.Failure("SESSION_REVOKED");
        if (!await worlds.GameplayFences(new WorldId(head.WorldId)).RenewAsync(new(accountId, sessionId, fence), until, cancellationToken))
            return GameSessionLeaseReply.Failure("BARRIER_PENDING");
        valid = await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken);
        return valid is { } current && current.Head.State == GameSessionState.Active
            ? Reply(current.Head, current.Root) : GameSessionLeaseReply.Failure("SESSION_REVOKED");
    }
    public async Task<GameSessionLeaseReply> EndAsync(string serverId, AccountId accountId, Guid sessionId, long fence, CancellationToken cancellationToken)
    {
        // Cleanup needs no live context: logout/recovery/ownership loss are reasons to flush and end.
        var definition = workloads.Value.Servers.SingleOrDefault(s => s.ServerId == serverId);
        var head = await sessions.FindAsync(accountId, cancellationToken);
        if (definition is null || head is null || head.ServerId != serverId || head.WorldId != definition.WorldId ||
            head.GameSessionId != sessionId || head.FencingToken != fence || sessionId == Guid.Empty || fence <= 0)
            return GameSessionLeaseReply.Failure("SESSION_REPLACED");
        if (!await worlds.GameplayFences(new WorldId(head.WorldId)).EndAsync(new(accountId, sessionId, fence), cancellationToken))
            return GameSessionLeaseReply.Failure("BARRIER_PENDING");
        return await sessions.TryEndAsync(accountId, sessionId, fence, Now, cancellationToken)
            ? new() { State = "ended" } : GameSessionLeaseReply.Failure("SESSION_REPLACED");
    }
    public async Task<GameSessionLeaseReply> ActivateAsync(string serverId, AccountId accountId, Guid sessionId, long fence, CancellationToken cancellationToken)
    {
        var valid = await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken);
        if (valid is null) return GameSessionLeaseReply.Failure("SESSION_REVOKED");
        var (head, context, root) = valid.Value;
        var authority = new GameplayWriteAuthority(accountId, sessionId, fence);
        var until = Min(Now.AddSeconds(45), head.LicenseUntil, Deadline(context));
        if (head.State == GameSessionState.Pending)
        {
            if (head.PreviousWorldId is { } previous && previous != head.WorldId &&
                !await worlds.GameplayFences(new WorldId(previous)).AdvanceAsync(authority, true, until, cancellationToken))
                return GameSessionLeaseReply.Failure("BARRIER_PENDING");
            if (!await worlds.GameplayFences(new WorldId(head.WorldId)).AdvanceAsync(authority, false, until, cancellationToken))
                return GameSessionLeaseReply.Failure("BARRIER_PENDING");
            // An epoch/recovery/ban change during either world barrier must not activate SQL authority.
            if (await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken) is null)
                return GameSessionLeaseReply.Failure("SESSION_REVOKED");
            if (!await sessions.TryActivateAsync(accountId, sessionId, fence, Now, until, cancellationToken))
                return GameSessionLeaseReply.Failure("SESSION_REVOKED");
        }
        // Recover a crash after SQL activation by completing its same target guard, never a new reservation.
        valid = await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken);
        if (valid is null || valid.Value.Head.State != GameSessionState.Active) return GameSessionLeaseReply.Failure("SESSION_REVOKED");
        (head, context, root) = valid.Value;
        if (!await worlds.GameplayFences(new WorldId(head.WorldId)).ActivateAsync(authority, head.LeaseUntil, cancellationToken))
            return GameSessionLeaseReply.Failure("BARRIER_PENDING");
        valid = await ReadAsync(serverId, accountId, sessionId, fence, cancellationToken);
        return valid is { } current && current.Head.State == GameSessionState.Active
            ? Reply(current.Head, current.Root) : GameSessionLeaseReply.Failure("SESSION_REVOKED");
    }
    private async Task<(GameSession Head, GameContextRecord Context, Account Root)?> ReadAsync(string serverId, AccountId accountId,
        Guid sessionId, long fence, CancellationToken cancellationToken)
    {
        var definition = workloads.Value.Servers.SingleOrDefault(s => s.ServerId == serverId);
        if (definition is null || accountId.Value <= 0 || sessionId == Guid.Empty || fence <= 0) return null;
        var head = await sessions.FindAsync(accountId, cancellationToken);
        if (head is null || head.GameSessionId != sessionId || head.FencingToken != fence || head.ServerId != serverId ||
            head.WorldId != definition.WorldId || head.State == GameSessionState.Ended || head.LeaseUntil <= Now || head.LicenseUntil <= Now) return null;
        var context = await authorization.GetContextByIdAsync(head.GameContextId, true, cancellationToken);
        if (context is null || context.AccountId != accountId.Value || context.Environment != head.Environment ||
            context.CredentialsVersion != head.CredentialsVersion || context.SessionEpoch != head.SessionEpoch || Deadline(context) <= Now) return null;
        var root = await accounts.FindByIdAsync(accountId, false, cancellationToken);
        if (root is null || root.Status != AccountStatus.Active || root.IsLockedAt(Now) ||
            (root.AccessLevel & AccountAccessLevel.Player) == 0 || root.CredentialsVersion != head.CredentialsVersion || root.SessionEpoch != head.SessionEpoch) return null;
        return (head, context, root);
    }
    private static DateTime Deadline(GameContextRecord context) => Min(context.AuthorizationValidUntil!.Value, context.AbsoluteExpiresAt, context.IdentityVerifiedAt!.Value.AddMinutes(30));
    private static DateTime Min(params DateTime[] values) => values.Min();
    private static GameSessionLeaseReply Reply(GameSession head, Account root) => new()
    {
        State = "active", AccountId = head.AccountId.Value.ToString(CultureInfo.InvariantCulture), GameSessionId = head.GameSessionId.ToString("D"),
        GameContextId = head.GameContextId.ToString("D"), FencingToken = head.FencingToken.ToString(CultureInfo.InvariantCulture), ServerId = head.ServerId,
        WorldId = head.WorldId, AccessLevel = (ushort)root.AccessLevel, CredentialsVersion = head.CredentialsVersion,
        SessionEpoch = head.SessionEpoch.ToString(CultureInfo.InvariantCulture), LeaseUntil = head.LeaseUntil, AuthorizationUntil = head.LicenseUntil,
    };
}
