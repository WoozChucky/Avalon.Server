using System.Text;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Database.Auth;
using Avalon.Domain.Auth;
using OtpNet;

namespace Avalon.Api.Services;

public interface IMFAService
{
    Task<SetupMFAResponse> Setup2FA(Account account, CancellationToken cancellationToken = default);
    Task VerifyMFA(string code, Account account, CancellationToken cancellationToken = default);
    Task<ConfirmMFAResponse> ConfirmMFA(string code, Account authContextAccount, CancellationToken cancellationToken = default);
    Task<SetupMFAResponse> ResetMFA(ResetMFARequest request, Account authContextAccount, CancellationToken cancellationToken = default);
}

public class MFAService : IMFAService
{
    private readonly ILogger<MFAService> _logger;
    private readonly IMFASetupRepository _mfaSetupRepository;
    private readonly AuthenticationConfig _authenticationConfig;

    public MFAService(ILoggerFactory loggerFactory, IMFASetupRepository mfaSetupRepository, AuthenticationConfig authenticationConfig)
    {
        _logger = loggerFactory.CreateLogger<MFAService>();
        _mfaSetupRepository = mfaSetupRepository;
        _authenticationConfig = authenticationConfig;
    }
    
    public async Task<SetupMFAResponse> Setup2FA(Account account, CancellationToken cancellationToken = default)
    {
        var existingMfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id!.Value);
        
        if (existingMfaSetup != null)
        {
            if (existingMfaSetup.Status == Status.Confirmed)
            {
                throw new BusinessException("MFA already setup");
            }
            
            if (existingMfaSetup.Status == Status.Setup && existingMfaSetup.CreatedAt.AddMinutes(5) >= DateTime.UtcNow)
            {
                throw new BusinessException("MFA setup already in progress");
            }
            
            if (existingMfaSetup.Status == Status.Setup && existingMfaSetup.CreatedAt.AddMinutes(5) < DateTime.UtcNow)
            {
                await _mfaSetupRepository.DeleteAsync(existingMfaSetup);
            }
        }
        
        var mfaSetup = new MFASetup()
        {
            Secret = KeyGeneration.GenerateRandomKey(32),
            RecoveryCode1 = Guid.NewGuid().ToByteArray(),
            RecoveryCode2 = Guid.NewGuid().ToByteArray(),
            RecoveryCode3 = Guid.NewGuid().ToByteArray(),
            AccountId = account.Id!.Value,
            Status = Status.Setup,
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = DateTime.MinValue,
        };
        
        mfaSetup = await _mfaSetupRepository.SaveAsync(mfaSetup);
        
        var uriString = new OtpUri(OtpType.Totp, mfaSetup.Secret, account.Email, _authenticationConfig.Issuer).ToString();
        
        return new SetupMFAResponse
        {
            Uri = uriString
        };
    }

    public async Task VerifyMFA(string code, Account account, CancellationToken cancellationToken = default)
    {
        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id!.Value);
        
        if (mfaSetup == null)
            throw new BusinessException("MFA not found");
        
        if (mfaSetup.Status != Status.Confirmed)
        {
            throw new BusinessException("MFA not confirmed");
        }
        
        var totp = new Totp(mfaSetup.Secret);
        
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
        {
            throw new BusinessException("Invalid code");
        }
    }

    public async Task<ConfirmMFAResponse> ConfirmMFA(string code, Account account, CancellationToken cancellationToken = default)
    {
        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id!.Value);
        
        if (mfaSetup == null)
            throw new BusinessException("MFA setup not found");
        
        if (mfaSetup.Status != Status.Setup)
        {
            throw new BusinessException("MFA setup already confirmed");
        }
        
        if (mfaSetup.CreatedAt.AddMinutes(5) < DateTime.UtcNow)
        {
            await _mfaSetupRepository.DeleteAsync(mfaSetup);
            throw new BusinessException("MFA setup expired");
        }
        
        var totp = new Totp(mfaSetup.Secret);
        
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
        {
            throw new BusinessException("Invalid code");
        }
        
        mfaSetup.Status = Status.Confirmed;
        mfaSetup.ConfirmedAt = DateTime.UtcNow;
        
        await _mfaSetupRepository.SaveAsync(mfaSetup);
        
        return new ConfirmMFAResponse
        {
            RecoveryCode1 = Encoding.UTF8.GetString(mfaSetup.RecoveryCode1),
            RecoveryCode2 = Encoding.UTF8.GetString(mfaSetup.RecoveryCode2),
            RecoveryCode3 = Encoding.UTF8.GetString(mfaSetup.RecoveryCode3),
        };
    }

    public async Task<SetupMFAResponse> ResetMFA(ResetMFARequest request, Account account, CancellationToken cancellationToken = default)
    {
        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id!.Value);
        
        if (mfaSetup == null)
            throw new BusinessException("MFA setup not found");
        
        if (mfaSetup.Status != Status.Confirmed)
        {
            throw new BusinessException("MFA not confirmed");
        }
        
        if (request.RecoveryCode1 != Encoding.UTF8.GetString(mfaSetup.RecoveryCode1)
            && request.RecoveryCode2 != Encoding.UTF8.GetString(mfaSetup.RecoveryCode2)
            && request.RecoveryCode3 != Encoding.UTF8.GetString(mfaSetup.RecoveryCode3))
        {
            throw new BusinessException("Invalid recovery codes");
        }
        
        await _mfaSetupRepository.DeleteAsync(mfaSetup);
        
        return await this.Setup2FA(account, cancellationToken);
    }
}
