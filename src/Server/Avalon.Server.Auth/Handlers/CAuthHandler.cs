using System.Globalization;
using System.Net;
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
        // answered for every username alike, so it says nothing about which ones exist.
        var sourceKey = CacheKeys.AuthSourceFailedLogins(RemoteAddress(ctx.Connection.RemoteEndPoint));
        if (await IsSourceOverLimitAsync(sourceKey))
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
            await CountSourceFailureAsync(sourceKey);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.INVALID_CREDENTIALS, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // Before the password check, so a locked account cannot be used to test passwords.
        var now = DateTime.UtcNow;
        if (account.IsLockedAt(now))
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        if (account.Locked)
        {
            // The lock has expired (#471). Lift it and start the failed-login count again.
            account.Locked = false;
            account.LockedUntil = null;
            account.FailedLogins = 0;
            await _accountRepository.UpdateAsync(account, token);
        }

        var verifier = Encoding.UTF8.GetString(account.Verifier);

        if (!_passwordVerifier.Verify(password, verifier))
        {
            account.LastAttemptIp = ctx.Connection.RemoteEndPoint.Split(':')[0];
            account.FailedLogins++;
            if (account.FailedLogins >= _authConfig.MaxFailedLoginAttempts)
            {
                account.Locked = true;
                account.LockedUntil = now.AddMinutes(_authConfig.LockoutDurationMinutes);
            }

            await _accountRepository.UpdateAsync(account, token);
            await CountSourceFailureAsync(sourceKey);

            ctx.Connection.Send(SAuthResultPacket.Create(null, null, account.Locked ? AuthResult.LOCKED : AuthResult.INVALID_CREDENTIALS, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

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

        ctx.Connection.AccountId = account.Id;

        account.Online = true;
        account.LastIp = ctx.Connection.RemoteEndPoint.Split(':')[0];
        account.LastLogin = DateTime.UtcNow;
        account.FailedLogins = 0;

        await _accountRepository.UpdateAsync(account, token);

        await _cache.PublishAsync(CacheKeys.AuthAccountsOnlineChannel, account.Id.ToString());

        ctx.Connection.Send(SAuthResultPacket.Create(account.Id, null, AuthResult.SUCCESS, ctx.Connection.CryptoSession.Encrypt));
    }

    private async Task<bool> IsSourceOverLimitAsync(string sourceKey)
    {
        var value = await _cache.GetAsync(sourceKey);
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var failures)
               && failures >= _authConfig.MaxFailedLoginsPerSource;
    }

    // A success never resets this count: otherwise one working account would let a source clear
    // its own limit between guesses at other accounts.
    private Task CountSourceFailureAsync(string sourceKey) =>
        _cache.IncrementAsync(sourceKey, TimeSpan.FromMinutes(_authConfig.FailedLoginSourceWindowMinutes));

    /// <summary>The address part of a remote endpoint, for IPv4 and IPv6 alike.</summary>
    private static string RemoteAddress(string remoteEndPoint) =>
        IPEndPoint.TryParse(remoteEndPoint, out var endPoint) ? endPoint.Address.ToString() : remoteEndPoint;
}
