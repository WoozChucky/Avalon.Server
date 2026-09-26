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
        var account = await PostLoginGuard.AccountOrCloseAsync(ctx.Connection, _accountRepository, _logger,
            "MFA setup", token);
        if (account == null)
            return;

        var result = await _mfaService.SetupMFAAsync(account, _authConfig.Issuer, token);

        ctx.Connection.Send(SMFASetupPacket.Create(
            result.OtpUri ?? string.Empty,
            result.Status,
            ctx.Connection.CryptoSession.Encrypt));
    }
}
