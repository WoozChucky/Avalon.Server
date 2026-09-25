using System.Text;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Services;
using Microsoft.Extensions.Options;

namespace Avalon.Server.Auth.Handlers;

public class CAuthHandler : IAuthPacketHandler<CAuthPacket>
{
    private readonly IAccountRepository _accountRepository;
    private readonly IReplicatedCache _cache;
    private readonly IMFAHashService _mfaHashService;
    private readonly IMfaSetupRepository _mfaSetupRepository;
    private readonly ILogger<CAuthHandler> _logger;
    private readonly AuthConfiguration _authConfig;
    private readonly IPasswordVerifier _passwordVerifier;

    public CAuthHandler(ILoggerFactory logger, IAccountRepository accountRepository, IReplicatedCache cache,
        IMFAHashService mfaHashService, IMfaSetupRepository mfaSetupRepository, IOptions<AuthConfiguration> options,
        IPasswordVerifier passwordVerifier)
    {
        _accountRepository = accountRepository;
        _cache = cache;
        _mfaHashService = mfaHashService;
        _mfaSetupRepository = mfaSetupRepository;
        _logger = logger.CreateLogger<CAuthHandler>();
        _authConfig = options.Value;
        _passwordVerifier = passwordVerifier;
    }

    public async Task ExecuteAsync(AuthPacketContext<CAuthPacket> ctx, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(ctx.Packet.Username) || string.IsNullOrWhiteSpace(ctx.Packet.Password))
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.INVALID_CREDENTIALS, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // Per source, ahead of any account (#471): a source that has failed too often is refused
        // before it can guess another password or push another account towards a lock. LOCKED
        // rather than INVALID_CREDENTIALS, so the player is told to wait instead of retyping; it is
        // answered for every username alike, so it says nothing about which ones exist. The slot is
        // taken here, before any work, and given back only once the password proves correct.
        var sourceKey = SourceBudget.KeyFor(ctx.Connection.RemoteEndPoint);
        if (!await SourceBudget.TryTakeAsync(_cache, _authConfig, sourceKey))
        {
            _logger.LogWarning("Login refused for source {SourceKey}: too many failed logins", sourceKey);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var password = ctx.Packet.Password.Trim();
        var account = await _accountRepository.FindByUserNameAsync(ctx.Packet.Username.ToUpperInvariant().Trim(), token);

        if (account == null)
        {
            // Pay for one BCrypt verify, as a wrong password on a real account does, so the time
            // taken does not tell an unknown username from a known one (#471).
            _passwordVerifier.Verify(password, BCryptPasswordVerifier.UnknownAccountHash);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.INVALID_CREDENTIALS, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // Before the password check, so a locked account cannot be used to test passwords. The
        // source slot taken above is kept, so probing for locked accounts is not free.
        var now = DateTime.UtcNow;
        if (account.IsLockedAt(now))
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var verifier = Encoding.UTF8.GetString(account.Verifier);

        if (!_passwordVerifier.Verify(password, verifier))
        {
            // Reply first, then write: an unknown username writes nothing, so a write ahead of the
            // reply would make a known one measurably slower. The answer is the one this failure
            // leads to from the row as read (an expired lock counts from zero); the write itself
            // is atomic, so a concurrent failure the read missed is still counted, and at worst
            // its lock is reported on the next attempt instead of this one.
            var expiredLock = account.Locked;
            var failures = (expiredLock ? 0 : account.FailedLogins) + 1;
            var result = failures >= _authConfig.MaxFailedLoginAttempts ? AuthResult.LOCKED : AuthResult.INVALID_CREDENTIALS;
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, result, ctx.Connection.CryptoSession.Encrypt));

            await _accountRepository.RecordFailedLoginAsync(account.Id, RemoteAddress.Of(ctx.Connection.RemoteEndPoint),
                now, _authConfig.MaxFailedLoginAttempts, now.AddMinutes(_authConfig.LockoutDurationMinutes), token);
            return;
        }

        await SourceBudget.GiveBackAsync(_cache, sourceKey);

        // After the password check, so a wrong password cannot be used to probe for a ban (#462);
        // before MFA and before any success, so an inactive account never gets past this point.
        if (account.Status != AccountStatus.Active)
        {
            _logger.LogWarning("Account {AccountId} refused at login while {Status}", account.Id, account.Status);
            AuthResult refusal = account.Status == AccountStatus.Deactivated ? AuthResult.DEACTIVATED : AuthResult.BANNED;
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, refusal, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var mfa = await _mfaSetupRepository.FindByAccountIdAsync(account.Id, token);
        if (mfa is { Status: MfaSetupStatus.Confirmed })
        {
            var mfaHash = await _mfaHashService.GenerateHashAsync(account);
            ctx.Connection.Send(SAuthResultPacket.Create(null, mfaHash, AuthResult.MFA_REQUIRED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        if (account.Online)
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.ALREADY_CONNECTED, ctx.Connection.CryptoSession.Encrypt));

            await _cache.PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, account.Id.ToString());

            var connectedSession = ctx.Connection.Server.Connections.FirstOrDefault(c => c.AccountId == account.Id);
            if (connectedSession != null)
            {
                connectedSession.Close();
            }
            else
            {
                _logger.LogWarning("Account {AccountId} is online but no connection was found", account.Id);
                account.Online = false;
                await _accountRepository.UpdateAsync(account, token);
            }
            return;
        }

        // Written only while the account is not locked, in SQL: a lock set by failures after the
        // row was read is never written away by this success.
        var lastIp = RemoteAddress.Of(ctx.Connection.RemoteEndPoint);
        if (!await _accountRepository.TryRecordLoginAsync(account.Id, lastIp, DateTime.UtcNow, token))
        {
            _logger.LogWarning("Account {AccountId} was locked during its login", account.Id);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        ctx.Connection.AccountId = account.Id;

        account.Online = true;
        account.LastIp = lastIp;
        account.LastLogin = DateTime.UtcNow;
        account.FailedLogins = 0;
        account.Locked = false;
        account.LockedUntil = null;

        await _cache.PublishAsync(CacheKeys.AuthAccountsOnlineChannel, account.Id.ToString());

        ctx.Connection.Send(SAuthResultPacket.Create(account.Id, null, AuthResult.SUCCESS, ctx.Connection.CryptoSession.Encrypt));
    }

}
