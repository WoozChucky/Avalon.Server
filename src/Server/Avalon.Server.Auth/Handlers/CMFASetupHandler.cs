using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Server.Auth.Handlers;

public class CMFASetupHandler : IAuthPacketHandler<CMFASetupPacket>
{
    private readonly ILogger<CMFASetupHandler> _logger;
    private readonly IMFAService _mfaService;
    private readonly IAccountRepository _accountRepository;
    private readonly AuthConfiguration _authConfig;

    public CMFASetupHandler(ILoggerFactory loggerFactory, IMFAService mfaService,
        IAccountRepository accountRepository, IOptions<AuthConfiguration> options)
    {
        _logger = loggerFactory.CreateLogger<CMFASetupHandler>();
        _mfaService = mfaService;
        _accountRepository = accountRepository;
        _authConfig = options.Value;
    }

    public async Task ExecuteAsync(AuthPacketContext<CMFASetupPacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection.AccountId == null)
        {
            _logger.LogWarning("Unauthenticated connection attempted CMFASetup from {Endpoint}", ctx.Connection.RemoteEndPoint);
            ctx.Connection.Close();
            return;
        }

        var account = await _accountRepository.FindByIdAsync(ctx.Connection.AccountId, false, token);
        if (account == null)
        {
            _logger.LogWarning("Account not found for connection {Id}", ctx.Connection.Id);
            ctx.Connection.Close();
            return;
        }

        var result = await _mfaService.SetupMFAAsync(account, _authConfig.Issuer, token);

        ctx.Connection.Send(SMFASetupPacket.Create(
            result.OtpUri ?? string.Empty,
            result.Status,
            ctx.Connection.CryptoSession.Encrypt));
    }
}
