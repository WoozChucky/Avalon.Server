using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Server.Auth.Handlers;

public class CWorldListHandler : IAuthPacketHandler<CWorldListPacket>
{
    private readonly ILogger<CWorldListHandler> _logger;
    private readonly IWorldRepository _worldRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IWorldReadiness _readiness;
    private readonly TimeProvider _time;

    public CWorldListHandler(ILoggerFactory loggerFactory, IWorldRepository worldRepository,
        IAccountRepository accountRepository, IWorldReadiness readiness, TimeProvider? time = null)
    {
        _logger = loggerFactory.CreateLogger<CWorldListHandler>();
        _worldRepository = worldRepository;
        _accountRepository = accountRepository;
        _readiness = readiness;
        _time = time ?? TimeProvider.System;
    }

    public async Task ExecuteAsync(AuthPacketContext<CWorldListPacket> ctx, CancellationToken token = default)
    {
        Account? account = await PostLoginGuard.AccountOrCloseAsync(ctx.Connection, _accountRepository, _logger,
            "world list", token);
        if (account == null)
            return;

        List<Domain.Auth.World> worlds = await _worldRepository.FindAllAsync(false, token);

        // A mask test, never "<=": AccountAccessLevel is [Flags] (#447).
        worlds = worlds.Where(w => AccessLevels.ForWorld(w.AccessLevelRequired).Allows(account.AccessLevel)).ToList();

        var worldsInfo = new List<WorldInfo>(worlds.Count);
        DateTime nowUtc = _time.GetUtcNow().UtcDateTime;
        foreach (Domain.Auth.World w in worlds)
        {
            bool ready = await _readiness.IsReadyAsync(w.Id.Value, token);
            var state = new WorldMaintenanceState(w.MaintenanceEnabled, w.MaintenanceRevision,
                w.MaintenanceDeadlineUtc);
            worldsInfo.Add(new WorldInfo
            {
                Id = w.Id.Value,
                Name = w.Name,
                Type = (short)w.Type,
                AccessLevelRequired = (short)w.AccessLevelRequired,
                Host = w.Host,
                Port = w.Port,
                MinVersion = w.MinVersion,
                Version = w.Version,
                Status = (short)WorldReadiness.Resolve(state, ready, nowUtc),
            });
        }

        ctx.Connection.Send(SWorldListPacket.Create(worldsInfo.ToArray(), PacketEncoder.Shared));
    }
}
