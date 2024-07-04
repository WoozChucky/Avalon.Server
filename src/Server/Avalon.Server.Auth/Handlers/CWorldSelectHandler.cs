using Avalon.Auth.Database.Repositories;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Auth;

namespace Avalon.Server.Auth.Handlers;

public class CWorldSelectHandler : IAuthPacketHandler<CWorldSelectPacket>
{
    private readonly ILogger<CHandshakeHandler> _logger;
    private readonly IReplicatedCache _cache;
    private readonly IAccountRepository _accountRepository;
    private readonly IWorldRepository _worldRepository;
    
    public CWorldSelectHandler(ILoggerFactory loggerFactory, IReplicatedCache cache, IAccountRepository accountRepository, IWorldRepository worldRepository)
    {
        _logger = loggerFactory.CreateLogger<CHandshakeHandler>();
        _cache = cache;
        _accountRepository = accountRepository;
        _worldRepository = worldRepository;
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
        
        //TODO: Check if account already in a world
        //TODO: Properly generate world key
        // generate random data
        var worldKey = new byte[32];
        new Random().NextBytes(worldKey);
        
        account.SessionKey = worldKey;
        await _accountRepository.UpdateAsync(account);
        
        var worldKeyBase64 = Convert.ToBase64String(worldKey);
        
        await _cache.SetAsync($"world:{world.Id}:keys:{worldKeyBase64}", account.Id.ToString()!, TimeSpan.FromMinutes(5));
        await _cache.PublishAsync($"world:{world.Id}:select", $"account:{account.Id}:worldKey:{worldKeyBase64}");

        ctx.Connection.Send(SWorldSelectPacket.Create(worldKey, ctx.Connection.CryptoSession.Encrypt));
    }
}
