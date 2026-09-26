using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.Auth.Handlers;

public class CMFAResetHandler : IAuthPacketHandler<CMFAResetPacket>
{
    private readonly ILogger<CMFAResetHandler> _logger;
    private readonly IMFAService _mfaService;
    private readonly IAccountRepository _accountRepository;

    public CMFAResetHandler(ILoggerFactory loggerFactory, IMFAService mfaService, IAccountRepository accountRepository)
    {
        _logger = loggerFactory.CreateLogger<CMFAResetHandler>();
        _mfaService = mfaService;
        _accountRepository = accountRepository;
    }

    public async Task ExecuteAsync(AuthPacketContext<CMFAResetPacket> ctx, CancellationToken token = default)
    {
        var account = await PostLoginGuard.AccountOrCloseAsync(ctx.Connection, _accountRepository, _logger,
            "MFA reset", token);
        if (account == null)
            return;

        var result = await _mfaService.ResetMFAAsync(
            account.Id,
            ctx.Connection.CredentialsVersion,
            ctx.Packet.RecoveryCode1,
            ctx.Packet.RecoveryCode2,
            ctx.Packet.RecoveryCode3,
            token);

        if (result.CredentialsChanged)
        {
            // Changed between the guard's read and the write (#495 re-review): as the guard would.
            _logger.LogWarning("Account {AccountId} MFA reset refused: its credentials changed", account.Id);
            ctx.Connection.Close();
            return;
        }

        ctx.Connection.Send(SMFAResetPacket.Create(result.Status, ctx.Connection.CryptoSession.Encrypt));
    }
}
