using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
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

    public ExchangeWorldKeyHandler(ILogger<ExchangeWorldKeyHandler> logger, IReplicatedCache cache,
        IAccountRepository accountRepository, IWorld world, IWorldRepository worldRepository)
    {
        _logger = logger;
        _cache = cache;
        _accountRepository = accountRepository;
        _world = world;
        _worldRepository = worldRepository;
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

        if (!long.TryParse(id, out long accountId))
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

        if (!await MayEnterAsync(account, token))
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

        ctx.Connection.CryptoSession.Initialize(ctx.Packet.PublicKey);

        ctx.Connection.AccountId = accountId;

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
