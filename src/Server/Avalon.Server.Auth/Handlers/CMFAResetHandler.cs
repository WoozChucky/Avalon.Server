using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.Auth.Handlers;

public class CMFAResetHandler : IAuthPacketHandler<CMFAResetPacket>
{
    private readonly ILogger<CMFAResetHandler> _logger;
    private readonly IMFAService _mfaService;

    public CMFAResetHandler(ILoggerFactory loggerFactory, IMFAService mfaService)
    {
        _logger = loggerFactory.CreateLogger<CMFAResetHandler>();
        _mfaService = mfaService;
    }

    public async Task ExecuteAsync(AuthPacketContext<CMFAResetPacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection.AccountId == null)
        {
            _logger.LogWarning("Unauthenticated connection attempted CMFAReset from {Endpoint}", ctx.Connection.RemoteEndPoint);
            ctx.Connection.Close();
            return;
        }

        var result = await _mfaService.ResetMFAAsync(
            ctx.Connection.AccountId,
            ctx.Packet.RecoveryCode1,
            ctx.Packet.RecoveryCode2,
            ctx.Packet.RecoveryCode3,
            token);

        ctx.Connection.Send(SMFAResetPacket.Create(result.Status, ctx.Connection.CryptoSession.Encrypt));
    }
}
