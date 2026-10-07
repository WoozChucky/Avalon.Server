using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
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

        Account account = attempt.Account!;

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

        string lastIp = attempt.Source.Ip;
        if (await GameLoginCompletion.TryStartAsync(ctx.Connection, account, lastIp, FailureResult(attempt),
                _accountRepository, _cache, _logger, token) is not null)
        {
            return;
        }

        // The login is complete: the source gets its own slot back, and the username's count is
        // cleared (owner decision on #484).
        await _policy.CompleteAsync(attempt);

        await GameLoginCompletion.FinishAsync(ctx.Connection, account, lastIp, _cache);
    }

    /// <summary>The answer to a wrong code in this attempt's budget slot.</summary>
    private AuthResult FailureResult(MfaCodeAttempt attempt) =>
        _policy.FailureLocks(attempt) ? AuthResult.LOCKED : AuthResult.MFA_FAILED;
}
