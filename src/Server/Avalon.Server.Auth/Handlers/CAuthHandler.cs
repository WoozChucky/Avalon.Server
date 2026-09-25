using System.Text;
using Avalon.Common.ValueObjects;
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

        // Per username, from every source (#484), and before the lookup, so a username no account
        // has is counted exactly like one that exists. The count this increment returns is this
        // attempt's place in the window, and it alone decides the lock: parallel guesses that all
        // read the row before the lock landed can no longer all be verified.
        var username = UsernameBudget.Normalise(ctx.Packet.Username);
        var usernameKey = UsernameBudget.KeyFor(username);
        long taken = await UsernameBudget.TakeAsync(_cache, _authConfig, usernameKey);
        if (UsernameBudget.Refuses(_authConfig, taken))
        {
            _logger.LogWarning("Login refused for a username past its failed-login limit");
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var password = ctx.Packet.Password.Trim();
        var account = await _accountRepository.FindByUserNameAsync(username, token);

        if (account == null)
        {
            // Pay for one BCrypt verify, as a wrong password on a real account does, so the time
            // taken does not tell an unknown username from a known one (#471); and answer, and
            // lock, from the same budget, so the replies do not either (#484).
            _passwordVerifier.Verify(password, BCryptPasswordVerifier.UnknownAccountHash);
            await FailAsync(ctx, null, usernameKey, taken, token);
            return;
        }

        // Before the password check, so a locked account cannot be used to test passwords. The
        // slots taken above are kept, so probing for locked accounts is not free.
        var now = DateTime.UtcNow;
        if (account.IsLockedAt(now))
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var verifier = Encoding.UTF8.GetString(account.Verifier);

        if (!_passwordVerifier.Verify(password, verifier))
        {
            await FailAsync(ctx, account.Id, usernameKey, taken, token);
            return;
        }

        await SourceBudget.GiveBackAsync(_cache, sourceKey);
        await UsernameBudget.GiveBackAsync(_cache, usernameKey);

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
                // Only the flag (#484): writing back the row as read would undo a lock or a ban
                // written since.
                _logger.LogWarning("Account {AccountId} is online but no connection was found", account.Id);
                account.Online = false;
                await _accountRepository.MarkOfflineAsync(account.Id, cancellationToken: token);
            }
            return;
        }

        // Written only while the account is not locked, in SQL: a lock set by failures after the
        // row was read is never written away by this success. The refusal is the answer a wrong
        // password in this attempt's place got (#484), so a parallel batch that crosses the lock
        // does not single out the right password by answering it differently.
        var lastIp = RemoteAddress.Of(ctx.Connection.RemoteEndPoint);
        if (!await _accountRepository.TryRecordLoginAsync(account.Id, lastIp, DateTime.UtcNow, token))
        {
            _logger.LogWarning("Account {AccountId} was locked during its login", account.Id);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, FailureResult(taken), ctx.Connection.CryptoSession.Encrypt));
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

    /// <summary>The answer to a wrong password in budget slot <paramref name="taken"/>.</summary>
    private AuthResult FailureResult(long taken) =>
        UsernameBudget.Locks(_authConfig, taken) ? AuthResult.LOCKED : AuthResult.INVALID_CREDENTIALS;

    /// <summary>
    /// A wrong password, or a username no account has. Reply first, then write: an unknown username
    /// writes nothing to the database, so a write ahead of the reply would make a known one
    /// measurably slower. The failure in the last slot locks the account and holds the budget for
    /// the lockout duration, for an unknown username as for a known one.
    /// </summary>
    private async Task FailAsync(AuthPacketContext<CAuthPacket> ctx, AccountId? accountId, string usernameKey, long taken,
        CancellationToken token)
    {
        bool locks = UsernameBudget.Locks(_authConfig, taken);
        ctx.Connection.Send(SAuthResultPacket.Create(null, null, FailureResult(taken), ctx.Connection.CryptoSession.Encrypt));

        var now = DateTime.UtcNow;
        if (locks)
        {
            await UsernameBudget.HoldLockAsync(_cache, _authConfig, usernameKey);
        }

        if (accountId != null)
        {
            await _accountRepository.RecordFailedLoginAsync(accountId, RemoteAddress.Of(ctx.Connection.RemoteEndPoint),
                now, locks ? now.AddMinutes(_authConfig.LockoutDurationMinutes) : null, token);
        }
    }
}
