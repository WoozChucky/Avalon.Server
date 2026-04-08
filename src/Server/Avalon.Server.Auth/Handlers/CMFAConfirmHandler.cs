using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.Auth.Handlers;

public class CMFAConfirmHandler : IAuthPacketHandler<CMFAConfirmPacket>
{
    private readonly ILogger<CMFAConfirmHandler> _logger;
    private readonly IMFAService _mfaService;

    public CMFAConfirmHandler(ILoggerFactory loggerFactory, IMFAService mfaService)
    {
        _logger = loggerFactory.CreateLogger<CMFAConfirmHandler>();
        _mfaService = mfaService;
    }

    public async Task ExecuteAsync(AuthPacketContext<CMFAConfirmPacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection.AccountId == null)
        {
            _logger.LogWarning("Unauthenticated connection attempted CMFAConfirm from {Endpoint}", ctx.Connection.RemoteEndPoint);
            ctx.Connection.Close();
            return;
        }

        var result = await _mfaService.ConfirmMFAAsync(ctx.Connection.AccountId, ctx.Packet.Code, token);

        ctx.Connection.Send(SMFAConfirmPacket.Create(
            result.RecoveryCodes ?? [],
            result.Status,
            ctx.Connection.CryptoSession.Encrypt));
    }
}
