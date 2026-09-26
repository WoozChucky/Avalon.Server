using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Network.Packets.Auth;
using Microsoft.Extensions.Logging;
using OtpNet;

namespace Avalon.Infrastructure.Services;

public class MFAService : IMFAService
{
    // One step either side of now, about ±30 s of clock drift (#471).
    private static readonly VerificationWindow TotpWindow = new(1, 1);

    private readonly ILogger<MFAService> _logger;
    private readonly IMfaSetupRepository _mfaSetupRepository;
    private readonly IMFAHashService _mfaHashService;
    private readonly ISecureRandom _secureRandom;
    private readonly IReplicatedCache _cache;

    public MFAService(ILoggerFactory loggerFactory, IMfaSetupRepository mfaSetupRepository, IMFAHashService mfaHashService,
        ISecureRandom secureRandom, IReplicatedCache cache)
    {
        _logger = loggerFactory.CreateLogger<MFAService>();
        _mfaSetupRepository = mfaSetupRepository;
        _mfaHashService = mfaHashService;
        _secureRandom = secureRandom;
        _cache = cache;
    }

    public async Task<MFASetupResult> SetupMFAAsync(Account account, string issuer, CancellationToken cancellationToken = default)
    {
        var existingMfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id, cancellationToken);

        if (existingMfaSetup is { Status: MfaSetupStatus.Confirmed })
            return new MFASetupResult(false, null, MFAOperationResult.AlreadyEnabled);

        var mfaSetup = new MFASetup
        {
            Secret = KeyGeneration.GenerateRandomKey(32),
            // Recovery codes are issued at confirm, the only time they are shown. Until then the
            // row holds no code, and an empty value never verifies.
            RecoveryCode1 = [],
            RecoveryCode2 = [],
            RecoveryCode3 = [],
            AccountId = account.Id,
            Status = MfaSetupStatus.Setup,
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = DateTime.MinValue,
        };

        // One row per account (#470): a stale or in-progress setup is replaced in place, never
        // joined by a second row, and a row confirmed meanwhile by another request is left alone.
        if (!await _mfaSetupRepository.UpsertPendingAsync(mfaSetup, cancellationToken))
            return new MFASetupResult(false, null, MFAOperationResult.AlreadyEnabled);

        var uri = new OtpUri(OtpType.Totp, mfaSetup.Secret, account.Email, issuer).ToString();
        return new MFASetupResult(true, uri, MFAOperationResult.Success);
    }

    public async Task<MFAConfirmResult> ConfirmMFAAsync(AccountId accountId, string code, CancellationToken cancellationToken = default)
    {
        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(accountId, cancellationToken);

        if (mfaSetup == null || mfaSetup.Status != MfaSetupStatus.Setup)
            return new MFAConfirmResult(false, null, MFAOperationResult.Error);

        if (mfaSetup.CreatedAt.AddMinutes(5) < DateTime.UtcNow)
        {
            // Only the expired setup this request read: a newer setup may have replaced it in place.
            await _mfaSetupRepository.DeletePendingAsync(mfaSetup.Id, mfaSetup.Secret, cancellationToken);
            return new MFAConfirmResult(false, null, MFAOperationResult.Error);
        }

        var totp = new Totp(mfaSetup.Secret);
        if (!totp.VerifyTotp(code, out var step, TotpWindow))
            return new MFAConfirmResult(false, null, MFAOperationResult.InvalidCode);

        // Generate the codes here and return them once; only their hashes are stored.
        var codes = new string[MFARecoveryCodes.Count];
        for (var i = 0; i < codes.Length; i++)
            codes[i] = MFARecoveryCodes.Generate(_secureRandom);

        // Conditional on the row still being the Setup row, with the secret, that this code was
        // verified against (#470). A double-submitted confirm, or a setup that replaced the secret
        // meanwhile, loses here instead of overwriting codes another response already showed.
        var confirmed = await _mfaSetupRepository.TryConfirmAsync(mfaSetup.Id, mfaSetup.Secret,
            MFARecoveryCodes.Hash(codes[0])!, MFARecoveryCodes.Hash(codes[1])!, MFARecoveryCodes.Hash(codes[2])!,
            DateTime.UtcNow, step, cancellationToken);
        if (!confirmed)
            return new MFAConfirmResult(false, null, MFAOperationResult.Error);

        return new MFAConfirmResult(true, codes, MFAOperationResult.Success);
    }

    public async Task<MFAVerifyResult> VerifyMFAAsync(string hash, string code, CancellationToken cancellationToken = default)
    {
        var accountId = await _mfaHashService.GetAccountIdAsync(hash);
        if (accountId == null)
            return new MFAVerifyResult(false, null);

        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(accountId, cancellationToken);
        if (mfaSetup == null || mfaSetup.Status != MfaSetupStatus.Confirmed)
            return new MFAVerifyResult(false, null);

        var totp = new Totp(mfaSetup.Secret);
        if (!totp.VerifyTotp(code, out var step, TotpWindow))
            return new MFAVerifyResult(false, null);

        // One winner per hash (#478): the DEL spends the hash, not the read above, as #450 does for
        // world keys. Two verifies of one hash, each with a code the window accepts (this step's
        // and the previous one's), both passed the step check below when the earlier step went
        // first, and both got a session. Only the caller whose delete removed the hash goes on;
        // a right code spends the hash even if its step is refused next, so a refused code cannot
        // be retried on it.
        if (!await _mfaHashService.TryConsumeAsync(hash, accountId))
        {
            _logger.LogWarning("Refused an MFA code for account {AccountId}: its hash was already spent", accountId);
            return new MFAVerifyResult(false, null);
        }

        // Each code once (#471): refuse a step no later than the last one accepted. The write is
        // conditional on the same, so two requests racing with one code cannot both pass.
        if (step <= mfaSetup.LastAcceptedTotpStep
            || !await _mfaSetupRepository.TryAcceptTotpStepAsync(mfaSetup.Id, step, cancellationToken))
        {
            _logger.LogWarning("Refused a reused TOTP code for account {AccountId}", accountId);
            return new MFAVerifyResult(false, null);
        }

        return new MFAVerifyResult(true, accountId);
    }

    public async Task<MFAResetResult> ResetMFAAsync(AccountId accountId, string r1, string r2, string r3, CancellationToken cancellationToken = default)
    {
        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(accountId, cancellationToken);

        if (mfaSetup == null)
            return new MFAResetResult(false, MFAOperationResult.NotEnabled);

        if (mfaSetup.Status != MfaSetupStatus.Confirmed)
            return new MFAResetResult(false, MFAOperationResult.NotEnabled);

        // Hash each input and compare in constant time. Non-short-circuiting '&' so every code is
        // checked whichever one is wrong. A stored value that is not a hash (a pre-#464 plaintext
        // code) never matches, which is how those codes are invalidated.
        var valid = MFARecoveryCodes.Matches(r1, mfaSetup.RecoveryCode1)
                    & MFARecoveryCodes.Matches(r2, mfaSetup.RecoveryCode2)
                    & MFARecoveryCodes.Matches(r3, mfaSetup.RecoveryCode3);
        if (!valid)
            return new MFAResetResult(false, MFAOperationResult.InvalidCode);

        // Deleting the setup consumes the codes: they cannot be used again. In the same transaction
        // every refresh token and personal access token the account holds is revoked (#483), as the
        // admin removal does, so no session opened before the reset outlives it. False when a
        // concurrent reset deleted the row first.
        if (!await _mfaSetupRepository.ResetConfirmedAsync(mfaSetup.Id, accountId, DateTime.UtcNow, cancellationToken))
            return new MFAResetResult(false, MFAOperationResult.NotEnabled);

        _logger.LogInformation("Account {AccountId} reset its MFA; its refresh and personal access tokens were revoked",
            accountId.Value);

        // Best-effort, as after the admin removal: the reset is committed, so a Redis failure is
        // logged and the reset still succeeds.
        try
        {
            await _cache.PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, accountId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not publish a world disconnect for account {AccountId} after its MFA reset",
                accountId.Value);
        }

        return new MFAResetResult(true, MFAOperationResult.Success);
    }

    public async Task<bool> IsEnrolledAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        var setup = await _mfaSetupRepository.FindByAccountIdAsync(accountId, cancellationToken);
        return setup is { Status: MfaSetupStatus.Confirmed };
    }
}
