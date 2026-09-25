using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Server.Auth.Handlers;

public class CMFAVerifyHandler : IAuthPacketHandler<CMFAVerifyPacket>
{
    private readonly ILogger<CMFAVerifyHandler> _logger;
    private readonly IMFAService _mfaService;
    private readonly IAccountRepository _accountRepository;
    private readonly IReplicatedCache _cache;
    private readonly IMFAHashService _mfaHashService;
    private readonly AuthConfiguration _authConfig;

    public CMFAVerifyHandler(ILoggerFactory loggerFactory, IMFAService mfaService,
        IAccountRepository accountRepository, IReplicatedCache cache, IMFAHashService mfaHashService,
        IOptions<AuthConfiguration> options)
    {
        _mfaHashService = mfaHashService;
        _authConfig = options.Value;
        _logger = loggerFactory.CreateLogger<CMFAVerifyHandler>();
        _mfaService = mfaService;
        _accountRepository = accountRepository;
        _cache = cache;
    }

    public async Task ExecuteAsync(AuthPacketContext<CMFAVerifyPacket> ctx, CancellationToken token = default)
    {
        // A code attempt spends the source's budget like a password attempt (#471), taken before the
        // code is checked and given back only when it is right.
        var sourceKey = SourceBudget.KeyFor(ctx.Connection.RemoteEndPoint);
        if (!await SourceBudget.TryTakeAsync(_cache, _authConfig, sourceKey))
        {
            _logger.LogWarning("MFA verify refused for source {SourceKey}: too many failed attempts", sourceKey);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // And each MFA hash allows MaxFailedMfaAttempts codes, counted before the code is checked so
        // parallel attempts cannot exceed it. The last failure deletes the hash, and the client has to
        // log in with the password again, which the per-username and per-source limits govern.
        var hash = ctx.Packet.MfaHash;
        var hashAccountId = await _mfaHashService.GetAccountIdAsync(hash);
        var attempts = hashAccountId == null ? -1 : await _mfaHashService.RecordAttemptAsync(hashAccountId);

        // Fail closed on a hash that is gone: no reverse key, or a reverse key that outlived the
        // :mfa hash for a moment (an expiry, or an admin removing MFA deletes them one at a time).
        if (hashAccountId == null || attempts < 0)
        {
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.MFA_FAILED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        if (attempts > _authConfig.MaxFailedMfaAttempts)
        {
            await _mfaHashService.CleanupHash(hash);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.MFA_FAILED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var account = await _accountRepository.FindByIdAsync(hashAccountId, false, token);
        if (account == null)
        {
            _logger.LogWarning("Account {AccountId} of an MFA hash was not found", hashAccountId);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.MFA_FAILED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // A code spends the username's budget as a password does (#484), before it is checked: the
        // budget is the account lock, and a fresh password login makes a fresh hash, so the per-hash
        // cap alone resets on every login.
        var usernameKey = UsernameBudget.KeyFor(account.Username);
        long taken = await UsernameBudget.TakeAsync(_cache, _authConfig, usernameKey);
        if (UsernameBudget.Refuses(_authConfig, taken))
        {
            _logger.LogWarning("MFA verify for account {AccountId} refused: too many failed logins", account.Id);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // The same for a lock (#471): failed logins inside the hash's two minutes can lock the
        // account after its password step passed. Before the code check, as for a password.
        if (account.IsLockedAt(DateTime.UtcNow))
        {
            _logger.LogWarning("Account {AccountId} refused at MFA verify while locked", account.Id);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var result = await _mfaService.VerifyMFAAsync(hash, ctx.Packet.Code, token);

        if (!result.Success || result.AccountId != account.Id)
        {
            if (attempts >= _authConfig.MaxFailedMfaAttempts)
            {
                _logger.LogWarning("MFA hash for account {AccountId} spent after {Attempts} wrong codes", hashAccountId, attempts);
                await _mfaHashService.CleanupHash(hash);
            }

            bool locks = UsernameBudget.Locks(_authConfig, taken);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, FailureResult(taken), ctx.Connection.CryptoSession.Encrypt));

            // Written after the reply, as a wrong password is. The failure in the budget's last
            // slot locks the account; its end is taken before the hold, so the row's lock ends
            // first, and the row is written whatever the hold does (the error still propagates,
            // and the server closes the connection).
            var now = DateTime.UtcNow;
            DateTime? lockUntil = locks ? now.AddMinutes(_authConfig.LockoutDurationMinutes) : null;
            try
            {
                if (locks)
                {
                    await UsernameBudget.HoldLockAsync(_cache, _authConfig, usernameKey);
                }
            }
            finally
            {
                await _accountRepository.RecordFailedLoginAsync(account.Id, RemoteAddress.Of(ctx.Connection.RemoteEndPoint),
                    now, lockUntil, token);
            }
            return;
        }

        // Both slots stay taken until the login is recorded (#484 review): a right code refused
        // below keeps its slots exactly as a wrong one does.

        // The same refusal as CAuthHandler (#462): the MFA hash outlives the password step by two
        // minutes, so an account banned or deactivated inside that window is caught here.
        if (account.Status != AccountStatus.Active)
        {
            _logger.LogWarning("Account {AccountId} refused at MFA verify while {Status}", account.Id, account.Status);
            AuthResult refusal = account.Status == AccountStatus.Deactivated ? AuthResult.DEACTIVATED : AuthResult.BANNED;
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, refusal, ctx.Connection.CryptoSession.Encrypt));
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

        // Written only while the account is not locked, in SQL: a lock set after the row was read is
        // never written away by this success. An expired lock is lifted with the count it was set by.
        // On this path (Active, offline) the refusal is the answer a wrong code in this attempt's
        // slot got, and both slots stay taken (#484), so a parallel batch that crosses the lock does
        // not single out the right code. ALREADY_CONNECTED and BANNED/DEACTIVATED above do single
        // it out, by design.
        var lastIp = RemoteAddress.Of(ctx.Connection.RemoteEndPoint);
        if (!await _accountRepository.TryRecordLoginAsync(account.Id, lastIp, DateTime.UtcNow, token))
        {
            _logger.LogWarning("Account {AccountId} was locked during its MFA verify", account.Id);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, FailureResult(taken), ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // The login is complete: the source gets its own slot back, and the username's count is
        // cleared (owner decision on #484).
        await SourceBudget.GiveBackAsync(_cache, sourceKey);
        await UsernameBudget.ResetAsync(_cache, usernameKey);

        ctx.Connection.AccountId = account.Id;

        account.Online = true;
        account.LastIp = lastIp;
        account.LastLogin = DateTime.UtcNow;
        account.FailedLogins = 0;
        account.Locked = false;
        account.LockedUntil = null;

        await _cache.PublishAsync(CacheKeys.AuthAccountsOnlineChannel, account.Id.ToString()!);

        ctx.Connection.Send(SAuthResultPacket.Create(account.Id, null, AuthResult.SUCCESS, ctx.Connection.CryptoSession.Encrypt));
    }

    /// <summary>The answer to a wrong code in budget slot <paramref name="taken"/>.</summary>
    private AuthResult FailureResult(long taken) =>
        UsernameBudget.Locks(_authConfig, taken) ? AuthResult.LOCKED : AuthResult.MFA_FAILED;
}
