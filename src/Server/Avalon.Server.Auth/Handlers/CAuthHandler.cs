using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Server.Auth.Handlers;

public class CAuthHandler : IAuthPacketHandler<CAuthPacket>
{
    private readonly IAccountRepository _accountRepository;
    private readonly IReplicatedCache _cache;
    private readonly IMFAHashService _mfaHashService;
    private readonly IMfaSetupRepository _mfaSetupRepository;
    private readonly ILogger<CAuthHandler> _logger;
    private readonly PasswordLoginPolicy _policy;

    public CAuthHandler(ILoggerFactory logger, IAccountRepository accountRepository, IReplicatedCache cache,
        IMFAHashService mfaHashService, IMfaSetupRepository mfaSetupRepository, IOptions<AuthConfiguration> options,
        IPasswordVerifier passwordVerifier)
    {
        _accountRepository = accountRepository;
        _cache = cache;
        _mfaHashService = mfaHashService;
        _mfaSetupRepository = mfaSetupRepository;
        _logger = logger.CreateLogger<CAuthHandler>();
        // The rules the REST login shares (#478): budgets, dummy verify, lock, failure counting.
        _policy = new PasswordLoginPolicy(accountRepository, cache, options.Value, passwordVerifier, logger);
    }

    public async Task ExecuteAsync(AuthPacketContext<CAuthPacket> ctx, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(ctx.Packet.Username) || string.IsNullOrWhiteSpace(ctx.Packet.Password))
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.INVALID_CREDENTIALS, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // The source's slot, then the username's, both before the lookup (#471, #484); then the
        // lookup, a dummy verify for an unknown username, the row's lock, and the verify. A refusal
        // past either budget is LOCKED for every username alike, so it says nothing about which
        // ones exist; a locked row is LOCKED before its password is checked, so a locked account
        // cannot be used to test passwords. Every slot taken is kept unless the attempt ends below
        // with an MFA hash or a completed login (#484).
        PasswordAttempt attempt = await _policy.CheckAsync(ctx.Packet.Username, ctx.Packet.Password,
            LoginSource.FromEndPoint(ctx.Connection.RemoteEndPoint), token);

        if (attempt.Refused)
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        if (attempt.Failed)
        {
            await FailAsync(ctx, attempt, token);
            return;
        }

        var account = attempt.Account!;

        // Both slots stay taken until the login is recorded, or an MFA hash is issued: a right
        // password refused below keeps its slots exactly as a wrong one does (#484 review).

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
            // Only its own slots back, and no reset: the login is not complete until the code is
            // accepted, and each password login makes a fresh hash with fresh code attempts.
            await _policy.GiveBackAsync(attempt);
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
                await _accountRepository.MarkOfflineAsync(account.Id, account.OnlineSessionId, cancellationToken: token);
            }
            return;
        }

        // Written only while the account is not locked, in SQL: a lock set by failures after the
        // row was read is never written away by this success. On this path (Active, offline, no
        // MFA) the refusal is the answer a wrong password in this attempt's slot got, and both
        // slots stay taken (#484), so a parallel batch that crosses the lock does not single out
        // the right password. MFA_REQUIRED, ALREADY_CONNECTED and BANNED/DEACTIVATED above do
        // single it out, by design.
        var lastIp = attempt.Source.Ip;
        if (!await _accountRepository.TryRecordLoginAsync(account.Id, lastIp, DateTime.UtcNow, ctx.Connection.Id, token))
        {
            _logger.LogWarning("Account {AccountId} was locked during its login", account.Id);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, FailureResult(attempt), ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // The login is complete: the source gets its own slot back, and the username's count is
        // cleared (owner decision on #484), so earlier typos do not carry over.
        await _policy.CompleteAsync(attempt);

        // The version of the row the proof was checked against (#495), before the account id
        // that makes the connection logged in.
        ctx.Connection.CredentialsVersion = account.CredentialsVersion;
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

    /// <summary>The answer to a wrong password in this attempt's budget slot.</summary>
    private AuthResult FailureResult(PasswordAttempt attempt) =>
        _policy.FailureLocks(attempt) ? AuthResult.LOCKED : AuthResult.INVALID_CREDENTIALS;

    /// <summary>
    /// A wrong password, or a username no account has. Reply first, then write: an unknown username
    /// writes nothing to the database, so a write ahead of the reply would make a known one
    /// measurably slower. The failure in the last slot locks the account and holds the budget for
    /// the lockout duration, for an unknown username as for a known one. The row is written
    /// whatever the hold does: a Redis error there must not leave the account unlocked. The error
    /// is logged, then rethrown after the write, and the server closes the connection.
    /// </summary>
    private async Task FailAsync(AuthPacketContext<CAuthPacket> ctx, PasswordAttempt attempt, CancellationToken token)
    {
        ctx.Connection.Send(SAuthResultPacket.Create(null, null, FailureResult(attempt), ctx.Connection.CryptoSession.Encrypt));
        await _policy.RecordFailureAsync(attempt, token);
    }
}
