using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.Auth.Handlers;

public class CMFAVerifyHandler : IAuthPacketHandler<CMFAVerifyPacket>
{
    private readonly ILogger<CMFAVerifyHandler> _logger;
    private readonly IMFAService _mfaService;
    private readonly IAccountRepository _accountRepository;
    private readonly IReplicatedCache _cache;

    public CMFAVerifyHandler(ILoggerFactory loggerFactory, IMFAService mfaService,
        IAccountRepository accountRepository, IReplicatedCache cache)
    {
        _logger = loggerFactory.CreateLogger<CMFAVerifyHandler>();
        _mfaService = mfaService;
        _accountRepository = accountRepository;
        _cache = cache;
    }

    public async Task ExecuteAsync(AuthPacketContext<CMFAVerifyPacket> ctx, CancellationToken token = default)
    {
        var result = await _mfaService.VerifyMFAAsync(ctx.Packet.MfaHash, ctx.Packet.Code, token);

        if (!result.Success)
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.MFA_FAILED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var account = await _accountRepository.FindByIdAsync(result.AccountId!);
        if (account == null)
        {
            _logger.LogWarning("Account {AccountId} not found after successful MFA verify", result.AccountId);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.MFA_FAILED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        ctx.Connection.AccountId = account.Id;

        account.Online = true;
        account.LastIp = ctx.Connection.RemoteEndPoint.Split(':')[0];
        account.LastLogin = DateTime.UtcNow;
        account.FailedLogins = 0;

        await _accountRepository.UpdateAsync(account);
        await _cache.PublishAsync(CacheKeys.AuthAccountsOnlineChannel, account.Id.ToString()!);

        ctx.Connection.Send(SAuthResultPacket.Create(account.Id, null, AuthResult.SUCCESS, ctx.Connection.CryptoSession.Encrypt));
    }
}
