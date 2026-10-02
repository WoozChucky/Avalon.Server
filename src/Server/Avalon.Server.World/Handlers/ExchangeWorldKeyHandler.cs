using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Generic;
using Avalon.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class ExchangeWorldKeyHandler : IWorldPacketHandler<CExchangeWorldKeyPacket>
{
    private readonly IAccountRepository _accountRepository;
    private readonly IReplicatedCache _cache;
    private readonly ILogger<ExchangeWorldKeyHandler> _logger;
    private readonly IWorld _world;
    private readonly IWorldRepository _worldRepository;
    private readonly IWorldMaintenanceRepository _maintenance;
    private readonly TimeProvider _clock;

    public ExchangeWorldKeyHandler(ILogger<ExchangeWorldKeyHandler> logger, IReplicatedCache cache,
        IAccountRepository accountRepository, IWorld world, IWorldRepository worldRepository,
        IWorldMaintenanceRepository maintenance, TimeProvider? clock = null)
    {
        _logger = logger;
        _cache = cache;
        _accountRepository = accountRepository;
        _world = world;
        _worldRepository = worldRepository;
        _maintenance = maintenance;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task ExecuteAsync(WorldPacketContext<CExchangeWorldKeyPacket> ctx, CancellationToken token = default)
    {
        // One exchange per connection. A second one is dropped before anything is read or spent, so it can never
        // overwrite the identity or the access level the first one published.
        if (ctx.Connection.AccountId is not null)
        {
            _logger.LogWarning("Client {EndPoint} sent a second world key exchange for account {AccountId}; ignored",
                ctx.Connection.RemoteEndPoint, ctx.Connection.AccountId);
            return;
        }

        if (await SpendKeyAsync(ctx, token) is not { } account)
            return;
        long accountId = account.Id.Value;

        if (!await MayEnterAsync(account, token))
            return;

        if (await MaintenanceDecisionAsync(ctx, account, token) is not { } decision)
            return;

        if (ctx.Packet.PublicKey.Length == 0)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid public key", ctx.Connection.RemoteEndPoint);
            return;
        }

        if (ctx.Packet.PublicKey.Length != ctx.Connection.ServerCrypto.GetValidKeySize())
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid public key size", ctx.Connection.RemoteEndPoint);
            return;
        }

        // Released only once accepted; a refused exchange leaves the mutex to expire on its TTL.
        await _cache.RemoveAsync(CacheKeys.AccountInWorld(accountId));

        if (RefusalAtAcceptance(account, decision.State, decision.ReadAtUtc) is { } refusal)
        {
            GracefulShutdownHelper.NotifyAndClose(ctx.Connection, refusal.Message, refusal.Reason, _logger);
            return;
        }

        ctx.Connection.CryptoSession.Initialize(ctx.Packet.PublicKey);

        // The maintenance deadline may arrive while an Admin is still at character selection.
        // Establish access before publishing identity, so the tick never drains a newly
        // authenticated Admin while the exchange is still finishing.
        if (ctx.Connection is IAccessLevelAssignable assignable)
            assignable.AssignAccessLevel(account.AccessLevel);
        ctx.Connection.AccountId = accountId;

        NetworkPacket result = SExchangeWorldKeyPacket.Create(
            ctx.Connection.ServerCrypto.GetPublicKey()
        );

        ctx.Connection.Send(result);
    }

    /// <summary>
    /// Reads and spends the presented world key and reads its account at the key's credentials version. Null, with
    /// the reason logged, when the key or the account does not hold.
    /// </summary>
    private async Task<Account?> SpendKeyAsync(WorldPacketContext<CExchangeWorldKeyPacket> ctx, CancellationToken token)
    {
        string worldKeyBase64 = Convert.ToBase64String(ctx.Packet.WorldKey);
        string worldKey = CacheKeys.WorldKey(_world.Id.Value, worldKeyBase64);

        string? id = await _cache.GetAsync(worldKey);
        if (id == null)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", ctx.Connection.RemoteEndPoint);
            return null;
        }

        // The DEL spends the key, not the GET (#450): two connections can both read it, but Redis
        // reports the delete to exactly one DEL. Spent before any other check, so a refused exchange
        // cannot be retried with it.
        if (!await _cache.RemoveAsync(worldKey))
        {
            _logger.LogWarning("Client {EndPoint} sent a world key that was already spent", ctx.Connection.RemoteEndPoint);
            return null;
        }

        if (!CacheKeys.TryParseWorldKeyValue(id, out long accountId, out int credentialsVersion))
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", ctx.Connection.RemoteEndPoint);
            return null;
        }

        Account? account = await _accountRepository.FindByIdAsync(accountId, false, token);
        if (account == null)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", ctx.Connection.RemoteEndPoint);
            return null;
        }

        // The key carries the version of the login that selected the world (#495). It lives five
        // minutes: a password change, an MFA reset or an admin's MFA removal inside them spends it.
        if (account.CredentialsVersion != credentialsVersion)
        {
            _logger.LogWarning("Account {AccountId} sent a world key issued before its credentials changed", account.Id);
            return null;
        }

        return account;
    }

    /// <summary>
    /// Reads the maintenance row once for this exchange. Answers null, having closed the connection, when the row
    /// cannot be read or the cutoff refuses a non-Admin.
    /// </summary>
    private async Task<(WorldMaintenanceState State, DateTime ReadAtUtc)?> MaintenanceDecisionAsync(
        WorldPacketContext<CExchangeWorldKeyPacket> ctx, Account account, CancellationToken token)
    {
        WorldMaintenanceState? maintenance;
        DateTime readAtUtc;
        try
        {
            maintenance = await _maintenance.ReadAsync(_world.Id, token);
            readAtUtc = _clock.GetUtcNow().UtcDateTime;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not read maintenance state for world {WorldId}; refusing entry",
                _world.Id);
            GracefulShutdownHelper.NotifyAndClose(ctx.Connection, "World temporarily unavailable",
                DisconnectReason.Unknown, _logger);
            return null;
        }

        if (maintenance is null)
        {
            GracefulShutdownHelper.NotifyAndClose(ctx.Connection, "World temporarily unavailable",
                DisconnectReason.Unknown, _logger);
            return null;
        }

        if (maintenance.IsCutoffActive(readAtUtc) && (account.AccessLevel & AccountAccessLevel.Admin) == 0)
        {
            GracefulShutdownHelper.NotifyAndClose(ctx.Connection, "World is under maintenance",
                DisconnectReason.Maintenance, _logger);
            return null;
        }

        return (maintenance, readAtUtc);
    }

    /// <summary>
    /// The last check before the exchange is accepted: the cutoff reached meanwhile is a maintenance refusal; a
    /// decision older than five seconds is not, and the client may simply select the world again. Null accepts.
    /// </summary>
    private (string Message, DisconnectReason Reason)? RefusalAtAcceptance(Account account,
        WorldMaintenanceState maintenance, DateTime readAtUtc)
    {
        DateTime acceptedAtUtc = _clock.GetUtcNow().UtcDateTime;
        if (maintenance.IsCutoffActive(acceptedAtUtc) && (account.AccessLevel & AccountAccessLevel.Admin) == 0)
            return ("World is under maintenance", DisconnectReason.Maintenance);

        if (acceptedAtUtc < readAtUtc.AddSeconds(5))
            return null;

        _logger.LogWarning("Account {AccountId} world key exchange outlived its maintenance decision; refusing",
            account.Id);
        return ("World temporarily unavailable", DisconnectReason.Unknown);
    }

    /// <summary>
    /// Access was checked when the key was issued, but the key lives five minutes (#450): an account
    /// banned, deactivated or demoted since then must not get in. The world row is read fresh rather
    /// than taken from the copy <see cref="IWorld"/> loaded at startup, so a world whose required
    /// level was raised since is enforced too.
    /// </summary>
    private async Task<bool> MayEnterAsync(Account account, CancellationToken token)
    {
        if (account.Status != AccountStatus.Active)
        {
            _logger.LogWarning("Account {AccountId} tried to enter world {WorldId} while {Status}",
                account.Id, _world.Id, account.Status);
            return false;
        }

        Domain.Auth.World? world = await _worldRepository.FindByIdAsync(_world.Id, false, token);
        if (world == null)
        {
            _logger.LogError("World {WorldId} has no world row; refusing account {AccountId}", _world.Id, account.Id);
            return false;
        }

        // The same rule as CWorldListHandler and CWorldSelectHandler: a mask test, never an ordinal one.
        if (!AccessLevels.ForWorld(world.AccessLevelRequired).Allows(account.AccessLevel))
        {
            _logger.LogWarning("Account {AccountId} tried to enter world {WorldId} without the required access level",
                account.Id, world.Id);
            return false;
        }

        return true;
    }
}
