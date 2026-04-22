using System.Text;
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

    public MFAService(ILoggerFactory loggerFactory, IMfaSetupRepository mfaSetupRepository, IMFAHashService mfaHashService)
    {
        _logger = loggerFactory.CreateLogger<MFAService>();
        _mfaSetupRepository = mfaSetupRepository;
        _mfaHashService = mfaHashService;
    }

    private static string GenerateRecoveryCode()
    {
        var hex = Guid.NewGuid().ToString("N").ToUpperInvariant();
        return $"{hex[..4]}-{hex[4..8]}-{hex[8..12]}";
    }

    public async Task<MFASetupResult> SetupMFAAsync(Account account, string issuer, CancellationToken cancellationToken = default)
    {
        var existingMfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id, cancellationToken);

        if (existingMfaSetup != null)
        {
            if (existingMfaSetup.Status == MfaSetupStatus.Confirmed)
                return new MFASetupResult(false, null, MFAOperationResult.AlreadyEnabled);

            // Delete stale or in-progress setup and recreate
            await _mfaSetupRepository.DeleteAsync(existingMfaSetup.Id, cancellationToken);
        }

        var mfaSetup = new MFASetup
        {
            Secret = KeyGeneration.GenerateRandomKey(32),
            RecoveryCode1 = Encoding.UTF8.GetBytes(GenerateRecoveryCode()),
            RecoveryCode2 = Encoding.UTF8.GetBytes(GenerateRecoveryCode()),
            RecoveryCode3 = Encoding.UTF8.GetBytes(GenerateRecoveryCode()),
            AccountId = account.Id,
            Status = MfaSetupStatus.Setup,
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = DateTime.MinValue,
        };

        mfaSetup = await _mfaSetupRepository.CreateAsync(mfaSetup, cancellationToken);

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
            await _mfaSetupRepository.DeleteAsync(mfaSetup.Id, cancellationToken);
            return new MFAConfirmResult(false, null, MFAOperationResult.Error);
        }

        var totp = new Totp(mfaSetup.Secret);
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
            return new MFAConfirmResult(false, null, MFAOperationResult.InvalidCode);

        mfaSetup.Status = MfaSetupStatus.Confirmed;
        mfaSetup.ConfirmedAt = DateTime.UtcNow;
        await _mfaSetupRepository.UpdateAsync(mfaSetup, cancellationToken);

        var codes = new[]
        {
            Encoding.UTF8.GetString(mfaSetup.RecoveryCode1),
            Encoding.UTF8.GetString(mfaSetup.RecoveryCode2),
            Encoding.UTF8.GetString(mfaSetup.RecoveryCode3)
        };
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

        if (r1 != Encoding.UTF8.GetString(mfaSetup.RecoveryCode1)
            || r2 != Encoding.UTF8.GetString(mfaSetup.RecoveryCode2)
            || r3 != Encoding.UTF8.GetString(mfaSetup.RecoveryCode3))
        {
            return new MFAResetResult(false, MFAOperationResult.InvalidCode);
        }

        await _mfaSetupRepository.DeleteAsync(mfaSetup.Id, cancellationToken);
        return new MFAResetResult(true, MFAOperationResult.Success);
    }

    public async Task<bool> IsEnrolledAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        var setup = await _mfaSetupRepository.FindByAccountIdAsync(accountId, cancellationToken);
        return setup is { Status: MfaSetupStatus.Confirmed };
    }
}
