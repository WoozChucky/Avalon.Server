using System.Text;
using Avalon.Auth.Database.Repositories;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;

namespace Avalon.Server.Auth.Handlers;


public class CAuthHandler : IAuthPacketHandler<CAuthPacket>
{
    private readonly IAccountRepository _accountRepository;
    private readonly IReplicatedCache _cache;
    private readonly IMFAHashService _mfaHashService;
    private readonly ILogger<CAuthHandler> _logger;
    
    
    public CAuthHandler(ILoggerFactory logger, IAccountRepository accountRepository, IReplicatedCache cache, IMFAHashService mfaHashService)
    {
        _accountRepository = accountRepository;
        _cache = cache;
        _mfaHashService = mfaHashService;
        _logger = logger.CreateLogger<CAuthHandler>();
    }
    
    public async Task ExecuteAsync(AuthPacketContext<CAuthPacket> ctx, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(ctx.Packet.Username) || string.IsNullOrWhiteSpace(ctx.Packet.Password))
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.INVALID_CREDENTIALS, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var account = await _accountRepository.FindByUserNameAsync(ctx.Packet.Username.ToUpperInvariant().Trim());

        if (account == null)
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.INVALID_CREDENTIALS, ctx.Connection.CryptoSession.Encrypt));
            return;
        }
        
        if (account.Locked)
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }
        
        var verifier = Encoding.UTF8.GetString(account.Verifier);

        if (!BCrypt.Net.BCrypt.Verify(ctx.Packet.Password.Trim(), verifier))
        {
            account.LastAttemptIp = ctx.Connection.RemoteEndPoint.Split(':')[0];
            account.FailedLogins++;
            if (account.FailedLogins >= 5) // TODO: Move this to a configuration
            {
                account.Locked = true;
            }
            
            await _accountRepository.UpdateAsync(account);
            
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, account.Locked ? AuthResult.LOCKED : AuthResult.INVALID_CREDENTIALS, ctx.Connection.CryptoSession.Encrypt));
            return;
        }
        
        /* TODO: Re-enable MFA feature
        var mfa = await _databaseManager.Auth.MFASetup.FindByAccountIdAsync(account.Id!.Value);
        if (mfa is { Status: Status.Confirmed })
        {
            var mfaHash = await _mfaHashService.GenerateHashAsync(account);
            ctx.Connection.Send(SAuthResultPacket.Create(null, mfaHash, AuthResult.MFA_REQUIRED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }
        */
        
        if (account.Online) 
        {
            //TODO: Properly implement this + broadcast to other servers
            
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.ALREADY_CONNECTED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }
        
        ctx.Connection.AccountId = account.Id;
        
        // account.Online = true;
        account.LastIp = ctx.Connection.RemoteEndPoint.Split(':')[0];
        account.LastLogin = DateTime.UtcNow;
        account.FailedLogins = 0;
        
        await _accountRepository.UpdateAsync(account);

        await _cache.PublishAsync($"auth:accounts:online", account.Id.ToString());
        
        ctx.Connection.Send(SAuthResultPacket.Create(account.Id, null, AuthResult.SUCCESS, ctx.Connection.CryptoSession.Encrypt));
    }
}
