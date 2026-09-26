using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Server.Auth.Handlers;

public class CMFAVerifyHandler : IAuthPacketHandler<CMFAVerifyPacket>
{
    private readonly ILogger<CMFAVerifyHandler> _logger;
    private readonly IAccountRepository _accountRepository;
    private readonly IReplicatedCache _cache;
    private readonly MfaLoginPolicy _policy;

    public CMFAVerifyHandler(ILoggerFactory loggerFactory, IMFAService mfaService,
        IAccountRepository accountRepository, IReplicatedCache cache, IMFAHashService mfaHashService,
        IOptions<AuthConfiguration> options)
    {
        _logger = loggerFactory.CreateLogger<CMFAVerifyHandler>();
        _accountRepository = accountRepository;
        _cache = cache;
        // The rules the REST MFA verify shares (#478): budgets, per-hash attempts, lock, one winner.
        _policy = new MfaLoginPolicy(accountRepository, cache, options.Value, mfaService, mfaHashService, loggerFactory);
    }

    public async Task ExecuteAsync(AuthPacketContext<CMFAVerifyPacket> ctx, CancellationToken token = default)
    {
        // The source's slot (#471), the hash's attempt count, the username's slot (#484) and the
        // row's lock, all before the code is checked. See MfaLoginPolicy.CheckAsync.
        MfaCodeAttempt attempt = await _policy.CheckAsync(ctx.Packet.MfaHash, ctx.Packet.Code,
            LoginSource.FromEndPoint(ctx.Connection.RemoteEndPoint), token);

        switch (attempt.Result)
        {
            case MfaCodeCheck.SourceRefused or MfaCodeCheck.UsernameRefused or MfaCodeCheck.Locked:
                ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
                return;
            case MfaCodeCheck.HashGone or MfaCodeCheck.HashSpent or MfaCodeCheck.AccountMissing or MfaCodeCheck.Replayed:
                ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.MFA_FAILED, ctx.Connection.CryptoSession.Encrypt));
                return;
            case MfaCodeCheck.WrongCode:
                ctx.Connection.Send(SAuthResultPacket.Create(null, null, FailureResult(attempt), ctx.Connection.CryptoSession.Encrypt));

                // Written after the reply, as a wrong password is. The failure in the budget's last
                // slot locks the account, and the row is written whatever the hold does (a hold error
                // is logged, rethrown after the write, and the server closes the connection).
                await _policy.RecordFailureAsync(attempt, token);
                return;
        }

        var account = attempt.Account!;

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
                await _accountRepository.MarkOfflineAsync(account.Id, account.OnlineSessionId, cancellationToken: token);
            }
            return;
        }

        // Written only while the account is not locked, in SQL: a lock set after the row was read is
        // never written away by this success. An expired lock is lifted with the count it was set by.
        // On this path (Active, offline) the refusal is the answer a wrong code in this attempt's
        // slot got, and both slots stay taken (#484), so a parallel batch that crosses the lock does
        // not single out the right code. ALREADY_CONNECTED and BANNED/DEACTIVATED above do single
        // it out, by design.
        var lastIp = attempt.Source.Ip;
        if (!await _accountRepository.TryRecordLoginAsync(account.Id, lastIp, DateTime.UtcNow, ctx.Connection.Id, token))
        {
            _logger.LogWarning("Account {AccountId} was locked during its MFA verify", account.Id);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, FailureResult(attempt), ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // The login is complete: the source gets its own slot back, and the username's count is
        // cleared (owner decision on #484).
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

        await _cache.PublishAsync(CacheKeys.AuthAccountsOnlineChannel, account.Id.ToString()!);

        ctx.Connection.Send(SAuthResultPacket.Create(account.Id, null, AuthResult.SUCCESS, ctx.Connection.CryptoSession.Encrypt));
    }

    /// <summary>The answer to a wrong code in this attempt's budget slot.</summary>
    private AuthResult FailureResult(MfaCodeAttempt attempt) =>
        _policy.FailureLocks(attempt) ? AuthResult.LOCKED : AuthResult.MFA_FAILED;
}
