using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Network.Packets.Auth;
using Microsoft.Extensions.Logging;
using OtpNet;

namespace Avalon.Infrastructure.Services;

public class MFAService : IMFAService
{
    private readonly ILogger<MFAService> _logger;
    private readonly IMfaSetupRepository _mfaSetupRepository;
    private readonly IMFAHashService _mfaHashService;
    private readonly ISecureRandom _secureRandom;

    public MFAService(ILoggerFactory loggerFactory, IMfaSetupRepository mfaSetupRepository, IMFAHashService mfaHashService, ISecureRandom secureRandom)
    {
        _logger = loggerFactory.CreateLogger<MFAService>();
        _mfaSetupRepository = mfaSetupRepository;
        _mfaHashService = mfaHashService;
        _secureRandom = secureRandom;
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
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
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
            DateTime.UtcNow, cancellationToken);
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
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
            return new MFAVerifyResult(false, null);

        await _mfaHashService.CleanupHash(hash);
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

        // Deleting the setup consumes the codes: they cannot be used again.
        await _mfaSetupRepository.DeleteAsync(mfaSetup.Id, cancellationToken);
        return new MFAResetResult(true, MFAOperationResult.Success);
    }

    public async Task<bool> IsEnrolledAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        var setup = await _mfaSetupRepository.FindByAccountIdAsync(accountId, cancellationToken);
        return setup is { Status: MfaSetupStatus.Confirmed };
    }
}
