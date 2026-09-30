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
        string worldKeyBase64 = Convert.ToBase64String(ctx.Packet.WorldKey);
        string worldKey = CacheKeys.WorldKey(_world.Id.Value, worldKeyBase64);

        string? id = await _cache.GetAsync(worldKey);
        if (id == null)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", ctx.Connection.RemoteEndPoint);
            return;
        }

        // The DEL spends the key, not the GET (#450): two connections can both read it, but Redis
        // reports the delete to exactly one DEL. Spent before any other check, so a refused exchange
        // cannot be retried with it.
        if (!await _cache.RemoveAsync(worldKey))
        {
            _logger.LogWarning("Client {EndPoint} sent a world key that was already spent", ctx.Connection.RemoteEndPoint);
            return;
        }

        if (!CacheKeys.TryParseWorldKeyValue(id, out long accountId, out int credentialsVersion))
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", ctx.Connection.RemoteEndPoint);
            return;
        }

        Account? account = await _accountRepository.FindByIdAsync(accountId, false, token);
        if (account == null)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", ctx.Connection.RemoteEndPoint);
            return;
        }

        // The key carries the version of the login that selected the world (#495). It lives five
        // minutes: a password change, an MFA reset or an admin's MFA removal inside them spends it.
        if (account.CredentialsVersion != credentialsVersion)
        {
            _logger.LogWarning("Account {AccountId} sent a world key issued before its credentials changed", account.Id);
            return;
        }

        if (!await MayEnterAsync(account, token))
            return;

        WorldMaintenanceState? maintenance;
        DateTime stateReadAtUtc;
        try
        {
            maintenance = await _maintenance.ReadAsync(_world.Id, token);
            stateReadAtUtc = _clock.GetUtcNow().UtcDateTime;
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
            return;
        }

        if (maintenance is null)
        {
            GracefulShutdownHelper.NotifyAndClose(ctx.Connection, "World temporarily unavailable",
                DisconnectReason.Unknown, _logger);
            return;
        }

        if (maintenance.IsCutoffActive(stateReadAtUtc) && (account.AccessLevel & AccountAccessLevel.Admin) == 0)
        {
            GracefulShutdownHelper.NotifyAndClose(ctx.Connection, "World is under maintenance",
                DisconnectReason.Maintenance, _logger);
            return;
        }

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

        DateTime acceptedAtUtc = _clock.GetUtcNow().UtcDateTime;
        if (acceptedAtUtc >= stateReadAtUtc.AddSeconds(5) ||
            (maintenance.IsCutoffActive(acceptedAtUtc) && (account.AccessLevel & AccountAccessLevel.Admin) == 0))
        {
            GracefulShutdownHelper.NotifyAndClose(ctx.Connection, "World is under maintenance",
                DisconnectReason.Maintenance, _logger);
            return;
        }

        ctx.Connection.CryptoSession.Initialize(ctx.Packet.PublicKey);

        ctx.Connection.AccountId = accountId;
        // The maintenance deadline may arrive while an Admin is still at character selection.
        // Establish access here, before a character has been selected, for the drain exemption.
        if (ctx.Connection is IAccessLevelAssignable assignable)
            assignable.AssignAccessLevel(account.AccessLevel);

        NetworkPacket result = SExchangeWorldKeyPacket.Create(
            ctx.Connection.ServerCrypto.GetPublicKey()
        );

        ctx.Connection.Send(result);
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
