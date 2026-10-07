using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Hosting.Telemetry;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Avalon.Network.Packets.Auth;
using Avalon.Server.Auth.Configuration;
using Avalon.Server.Auth.Telemetry;
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
            Record(ctx, LoginTelemetry.Tag(AuthResult.INVALID_CREDENTIALS), null);
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
            Record(ctx, LoginTelemetry.RateLimited, null);
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
            Record(ctx, LoginTelemetry.Tag(refusal), account.Id);
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
            Record(ctx, LoginTelemetry.Tag(AuthResult.MFA_REQUIRED), account.Id);
            return;
        }

        var lastIp = attempt.Source.Ip;
        AuthResult? startRefusal = await GameLoginCompletion.TryStartAsync(ctx.Connection, account, lastIp,
            FailureResult(attempt), _accountRepository, _cache, _logger, token);
        if (startRefusal is { } result)
        {
            Record(ctx, LoginTelemetry.Tag(result), account.Id);
            return;
        }

        // The login is complete: the source gets its own slot back, and the username's count is
        // cleared (owner decision on #484), so earlier typos do not carry over.
        await _policy.CompleteAsync(attempt);

        await GameLoginCompletion.FinishAsync(ctx.Connection, account, lastIp, _cache);
        Record(ctx, LoginTelemetry.Tag(AuthResult.SUCCESS), account.Id);
    }

    /// <summary>Counts the login by result and logs it, next to the reply that told the client.</summary>
    private void Record(AuthPacketContext<CAuthPacket> ctx, string result, AccountId? account)
    {
        DiagnosticsConfig.Auth.Logins.Add(1, new KeyValuePair<string, object?>("result", result));
        _logger.Log(LoginTelemetry.LogLevelFor(result), "Login {LoginResult} for account {AccountId} from {ClientAddress}",
            result, account?.Value, PacketTags.AddressOf(ctx.Connection.RemoteEndPoint));
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
        Record(ctx, LoginTelemetry.Tag(FailureResult(attempt)), attempt.Account?.Id);
        await _policy.RecordFailureAsync(attempt, token);
    }
}
