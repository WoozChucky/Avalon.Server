using Avalon.Auth.Database.Repositories;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Auth;

namespace Avalon.Server.Auth.Handlers;

public class CWorldListHandler : IAuthPacketHandler<CWorldListPacket>
{
    private readonly ILogger<CWorldListHandler> _logger;
    private readonly IWorldRepository _worldRepository;
    private readonly IAccountRepository _accountRepository;
    
    public CWorldListHandler(ILoggerFactory loggerFactory, IWorldRepository worldRepository, IAccountRepository accountRepository)
    {
        _logger = loggerFactory.CreateLogger<CWorldListHandler>();
        _worldRepository = worldRepository;
        _accountRepository = accountRepository;
    }
    
    public async Task ExecuteAsync(AuthPacketContext<CWorldListPacket> ctx, CancellationToken token = default)
    {
        var worlds = await _worldRepository.GetAllAsync();
        
        var account = await _accountRepository.FindByIdAsync(ctx.Connection.AccountId ?? 0);
        if (account == null)
        {
            _logger.LogWarning("Account not found for connection {Session}", ctx.Connection.Id);
            ctx.Connection.Close();
            return;
        }
        
        worlds = worlds.Where(w => w.AccessLevelRequired <= account.AccessLevel).ToList();
        
        var worldsInfo = worlds.Select(w => new WorldInfo
        {
            Id = w.Id!.Value,
            Name = w.Name,
            Type = (short) w.Type,
            AccessLevelRequired = (short) w.AccessLevelRequired,
            Host = w.Host,
            Port = w.Port,
            MinVersion = w.MinVersion,
            Version = w.Version,
            Status = (short) w.Status,
        }).ToArray();

        ctx.Connection.Send(SWorldListPacket.Create(worldsInfo, ctx.Connection.CryptoSession.Encrypt));
    }
}
