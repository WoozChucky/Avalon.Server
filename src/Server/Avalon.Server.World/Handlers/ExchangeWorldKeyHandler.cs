using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Auth;
using Avalon.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class ExchangeWorldKeyHandler : IWorldPacketHandler<CExchangeWorldKeyPacket>
{
    private readonly ILogger<ExchangeWorldKeyHandler> _logger;
    private readonly IReplicatedCache _cache;
    private readonly IAccountRepository _accountRepository;
    private readonly IWorld _world;

    public ExchangeWorldKeyHandler(ILogger<ExchangeWorldKeyHandler> logger, IReplicatedCache cache, IAccountRepository accountRepository, IWorld world)
    {
        _logger = logger;
        _cache = cache;
        _accountRepository = accountRepository;
        _world = world;
    }

    public async Task ExecuteAsync(WorldPacketContext<CExchangeWorldKeyPacket> ctx, CancellationToken token = default)
    {
        var worldKeyBase64 = Convert.ToBase64String(ctx.Packet.WorldKey);

        var id = await _cache.GetAsync($"world:{_world.Id}:keys:{worldKeyBase64}");
        if (id == null)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", ctx.Connection.RemoteEndPoint);
            return;
        }

        await _cache.RemoveAsync($"world:{_world.Id}:keys:{worldKeyBase64}");

        if (!ulong.TryParse(id, out var accountId))
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", ctx.Connection.RemoteEndPoint);
            return;
        }

        var account = await _accountRepository.FindByIdAsync(accountId);
        if (account == null)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid world key", ctx.Connection.RemoteEndPoint);
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

        ctx.Connection.CryptoSession.Initialize(ctx.Packet.PublicKey);

        ctx.Connection.AccountId = accountId;

        var result = SExchangeWorldKeyPacket.Create(
            ctx.Connection.ServerCrypto.GetPublicKey()
        );

        ctx.Connection.Send(result);
    }
}
