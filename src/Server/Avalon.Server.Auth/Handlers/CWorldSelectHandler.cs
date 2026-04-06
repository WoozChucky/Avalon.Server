using Avalon.Database.Auth.Repositories;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;

namespace Avalon.Server.Auth.Handlers;

public class CWorldSelectHandler : IAuthPacketHandler<CWorldSelectPacket>
{
    private readonly ILogger<CHandshakeHandler> _logger;
    private readonly IReplicatedCache _cache;
    private readonly IAccountRepository _accountRepository;
    private readonly IWorldRepository _worldRepository;
    private readonly ISecureRandom _secureRandom;

    public CWorldSelectHandler(ILoggerFactory loggerFactory, IReplicatedCache cache, IAccountRepository accountRepository, IWorldRepository worldRepository, ISecureRandom secureRandom)
    {
        _logger = loggerFactory.CreateLogger<CHandshakeHandler>();
        _cache = cache;
        _accountRepository = accountRepository;
        _worldRepository = worldRepository;
        _secureRandom = secureRandom;
    }

    public async Task ExecuteAsync(AuthPacketContext<CWorldSelectPacket> ctx, CancellationToken token = default)
    {

        var account = await _accountRepository.FindByIdAsync(ctx.Connection.AccountId ?? 0);
        if (account == null)
        {
            _logger.LogWarning("Account not found for connection {Session}", ctx.Connection.Id);
            ctx.Connection.Close();
            return;
        }

        var world = await _worldRepository.FindByIdAsync(ctx.Packet.WorldId);
        if (world == null)
        {
            _logger.LogWarning("World not found for id {WorldId}", ctx.Packet.WorldId);
            return;
        }

        if (world.AccessLevelRequired > account.AccessLevel)
        {
            _logger.LogWarning("Account {AccountId} tried to access world {WorldId} without the required access level", account.Id, world.Id);
            return;
        }

        bool sessionSlotAcquired = await _cache.SetNxAsync($"account:{account.Id}:inWorld", "1", TimeSpan.FromMinutes(5));
        if (!sessionSlotAcquired)
        {
            _logger.LogWarning("Account {AccountId} attempted to enter world {WorldId} while already holding an active session", account.Id, world.Id);
            ctx.Connection.Send(SWorldSelectPacket.CreateError(WorldSelectResult.DuplicateSession, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var worldKey = _secureRandom.GetBytes(32);

        account.SessionKey = worldKey;
        await _accountRepository.UpdateAsync(account);

        var worldKeyBase64 = Convert.ToBase64String(worldKey);

        await _cache.SetAsync($"world:{world.Id}:keys:{worldKeyBase64}", account.Id.ToString()!, TimeSpan.FromMinutes(5));
        await _cache.PublishAsync($"world:{world.Id}:select", $"account:{account.Id}:worldKey:{worldKeyBase64}");

        ctx.Connection.Send(SWorldSelectPacket.Create(worldKey, ctx.Connection.CryptoSession.Encrypt));
    }
}
