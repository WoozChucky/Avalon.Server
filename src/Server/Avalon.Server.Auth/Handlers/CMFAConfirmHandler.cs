using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.Auth.Handlers;

public class CMFAConfirmHandler : IAuthPacketHandler<CMFAConfirmPacket>
{
    private readonly ILogger<CMFAConfirmHandler> _logger;
    private readonly IMFAService _mfaService;
    private readonly IAccountRepository _accountRepository;

    public CMFAConfirmHandler(ILoggerFactory loggerFactory, IMFAService mfaService, IAccountRepository accountRepository)
    {
        _logger = loggerFactory.CreateLogger<CMFAConfirmHandler>();
        _mfaService = mfaService;
        _accountRepository = accountRepository;
    }

    public async Task ExecuteAsync(AuthPacketContext<CMFAConfirmPacket> ctx, CancellationToken token = default)
    {
        var account = await PostLoginGuard.AccountOrCloseAsync(ctx.Connection, _accountRepository, _logger,
            "MFA confirm", token);
        if (account == null)
            return;

        var result = await _mfaService.ConfirmMFAAsync(account.Id, ctx.Packet.Code, token);

        ctx.Connection.Send(SMFAConfirmPacket.Create(
            result.RecoveryCodes ?? [],
            result.Status,
            ctx.Connection.CryptoSession.Encrypt));
    }
}
