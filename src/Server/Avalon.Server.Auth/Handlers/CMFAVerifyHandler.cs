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
        // log in with the password again, which the per-account and per-source limits govern.
        var hash = ctx.Packet.MfaHash;
        var hashAccountId = await _mfaHashService.GetAccountIdAsync(hash);
        var attempts = hashAccountId == null ? -1 : await _mfaHashService.RecordAttemptAsync(hashAccountId);
        if (attempts > _authConfig.MaxFailedMfaAttempts)
        {
            await _mfaHashService.CleanupHash(hash);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.MFA_FAILED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        var result = await _mfaService.VerifyMFAAsync(hash, ctx.Packet.Code, token);

        if (!result.Success)
        {
            if (attempts >= _authConfig.MaxFailedMfaAttempts)
            {
                _logger.LogWarning("MFA hash for account {AccountId} spent after {Attempts} wrong codes", hashAccountId, attempts);
                await _mfaHashService.CleanupHash(hash);
            }

            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.MFA_FAILED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        await SourceBudget.GiveBackAsync(_cache, sourceKey);

        var account = await _accountRepository.FindByIdAsync(result.AccountId!, false, token);
        if (account == null)
        {
            _logger.LogWarning("Account {AccountId} not found after successful MFA verify", result.AccountId);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.MFA_FAILED, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // The same refusal as CAuthHandler (#462): the MFA hash outlives the password step by two
        // minutes, so an account banned or deactivated inside that window is caught here.
        if (account.Status != AccountStatus.Active)
        {
            _logger.LogWarning("Account {AccountId} refused at MFA verify while {Status}", account.Id, account.Status);
            AuthResult refusal = account.Status == AccountStatus.Deactivated ? AuthResult.DEACTIVATED : AuthResult.BANNED;
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, refusal, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        // The same for a lock (#471): failed logins inside that window can lock the account after
        // its password step passed.
        if (account.IsLockedAt(DateTime.UtcNow))
        {
            _logger.LogWarning("Account {AccountId} refused at MFA verify while locked", account.Id);
            ctx.Connection.Send(SAuthResultPacket.Create(null, null, AuthResult.LOCKED, ctx.Connection.CryptoSession.Encrypt));
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

        // Written only while the account is not locked, in SQL: a lock set after the row was read is
        // never written away by this success. An expired lock is lifted with the count it was set by.
        var lastIp = RemoteAddress.Of(ctx.Connection.RemoteEndPoint);
        if (!await _accountRepository.TryRecordLoginAsync(account.Id, lastIp, DateTime.UtcNow, token))
        {
            _logger.LogWarning("Account {AccountId} was locked during its MFA verify", account.Id);
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

        await _cache.PublishAsync(CacheKeys.AuthAccountsOnlineChannel, account.Id.ToString()!);

        ctx.Connection.Send(SAuthResultPacket.Create(account.Id, null, AuthResult.SUCCESS, ctx.Connection.CryptoSession.Encrypt));
    }
}
