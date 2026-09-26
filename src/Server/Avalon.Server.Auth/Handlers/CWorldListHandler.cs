using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
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
        var account = await PostLoginGuard.AccountOrCloseAsync(ctx.Connection, _accountRepository, _logger,
            "world list", token);
        if (account == null)
            return;

        var worlds = await _worldRepository.FindAllAsync(false, token);

        // A mask test, never "<=": AccountAccessLevel is [Flags] (#447).
        worlds = worlds.Where(w => AccessLevels.ForWorld(w.AccessLevelRequired).Allows(account.AccessLevel)).ToList();

        var worldsInfo = worlds.Select(w => new WorldInfo
        {
            Id = w.Id.Value,
            Name = w.Name,
            Type = (short)w.Type,
            AccessLevelRequired = (short)w.AccessLevelRequired,
            Host = w.Host,
            Port = w.Port,
            MinVersion = w.MinVersion,
            Version = w.Version,
            Status = (short)w.Status,
        }).ToArray();

        ctx.Connection.Send(SWorldListPacket.Create(worldsInfo, ctx.Connection.CryptoSession.Encrypt));
    }
}
