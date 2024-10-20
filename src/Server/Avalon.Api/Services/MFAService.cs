using System.Net;
using System.Security.Authentication;
using System.Text;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using OtpNet;

namespace Avalon.Api.Services;

public interface IMFAService
{
    Task<SetupMFAResponse> SetupMFA(Account account, CancellationToken cancellationToken = default);
    Task<AuthenticateResponse> VerifyMFA(VerifyMFARequest request, IPAddress ipAddress, CancellationToken cancellationToken = default);
    Task<ConfirmMFAResponse> ConfirmMFA(string code, Account authContextAccount, CancellationToken cancellationToken = default);
    Task<SetupMFAResponse> ResetMFA(ResetMFARequest request, Account authContextAccount, CancellationToken cancellationToken = default);
}

public class MFAService : IMFAService
{
    private readonly ILogger<MFAService> _logger;
    private readonly IMfaSetupRepository _mfaSetupRepository;
    private readonly AuthenticationConfig _authenticationConfig;
    private readonly IMFAHashService _mfaHashService;
    private readonly IJwtUtils _jwtUtils;
    private readonly IAccountRepository _accountRepository;

    public MFAService(ILoggerFactory loggerFactory, IMfaSetupRepository mfaSetupRepository, AuthenticationConfig authenticationConfig,
        IMFAHashService mfaHashService, IJwtUtils jwtUtils, IAccountRepository accountRepository)
    {
        _logger = loggerFactory.CreateLogger<MFAService>();
        _mfaSetupRepository = mfaSetupRepository;
        _authenticationConfig = authenticationConfig;
        _mfaHashService = mfaHashService;
        _jwtUtils = jwtUtils;
        _accountRepository = accountRepository;
    }
    
    public async Task<SetupMFAResponse> SetupMFA(Account account, CancellationToken cancellationToken = default)
    {
        var existingMfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id);
        
        if (existingMfaSetup != null)
        {
            if (existingMfaSetup.Status == MfaSetupStatus.Confirmed)
            {
                throw new BusinessException("MFA already setup");
            }
            
            if (existingMfaSetup.Status == MfaSetupStatus.Setup && existingMfaSetup.CreatedAt.AddMinutes(5) >= DateTime.UtcNow)
            {
                throw new BusinessException("MFA setup already in progress");
            }
            
            if (existingMfaSetup.Status == MfaSetupStatus.Setup && existingMfaSetup.CreatedAt.AddMinutes(5) < DateTime.UtcNow)
            {
                await _mfaSetupRepository.DeleteAsync(existingMfaSetup.Id);
            }
        }
        
        var mfaSetup = new MFASetup()
        {
            Secret = KeyGeneration.GenerateRandomKey(32),
            RecoveryCode1 = Guid.NewGuid().ToByteArray(),
            RecoveryCode2 = Guid.NewGuid().ToByteArray(),
            RecoveryCode3 = Guid.NewGuid().ToByteArray(),
            AccountId = account.Id,
            Status = MfaSetupStatus.Setup,
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = DateTime.MinValue,
        };
        
        mfaSetup = await _mfaSetupRepository.CreateAsync(mfaSetup);
        
        var uriString = new OtpUri(OtpType.Totp, mfaSetup.Secret, account.Email, _authenticationConfig.Issuer).ToString();
        
        return new SetupMFAResponse
        {
            Uri = uriString
        };
    }

    public async Task<AuthenticateResponse> VerifyMFA(VerifyMFARequest request, IPAddress ipAddress, CancellationToken cancellationToken = default)
    {
        var code = request.Code;
        
        var accountId = await _mfaHashService.GetAccountIdAsync(request.Hash);
        if (accountId == null)
        {
            throw new AuthenticationException("Login expired");
        }
        
        var account = await _accountRepository.FindByIdAsync(accountId.Value);
        if (account == null)
        {
            throw new AuthenticationException("Account not found");
        }
        
        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id);
        
        if (mfaSetup == null)
            throw new BusinessException("MFA not found");
        
        if (mfaSetup.Status != MfaSetupStatus.Confirmed)
        {
            throw new BusinessException("MFA not confirmed");
        }
        
        var totp = new Totp(mfaSetup.Secret);
        
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
        {
            throw new BusinessException("Invalid code");
        }
        
        await _mfaHashService.CleanupHash(request.Hash);
        
        account.LastIp = ipAddress.ToString();
        account.LastLogin = DateTime.UtcNow;
        
        await _accountRepository.UpdateAsync(account);
        
        return new AuthenticateResponse
        {
            Token = _jwtUtils.GenerateJwtToken(account),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            Status = AuthenticationResponseStatus.Success
        };
    }

    public async Task<ConfirmMFAResponse> ConfirmMFA(string code, Account account, CancellationToken cancellationToken = default)
    {
        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id);
        
        if (mfaSetup == null)
            throw new BusinessException("MFA setup not found");
        
        if (mfaSetup.Status != MfaSetupStatus.Setup)
        {
            throw new BusinessException("MFA setup already confirmed");
        }
        
        if (mfaSetup.CreatedAt.AddMinutes(5) < DateTime.UtcNow)
        {
            await _mfaSetupRepository.DeleteAsync(mfaSetup.Id);
            throw new BusinessException("MFA setup expired");
        }
        
        var totp = new Totp(mfaSetup.Secret);
        
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(2, 2)))
        {
            throw new BusinessException("Invalid code");
        }
        
        mfaSetup.Status = MfaSetupStatus.Confirmed;
        mfaSetup.ConfirmedAt = DateTime.UtcNow;
        
        await _mfaSetupRepository.UpdateAsync(mfaSetup);
        
        return new ConfirmMFAResponse
        {
            RecoveryCode1 = Encoding.UTF8.GetString(mfaSetup.RecoveryCode1),
            RecoveryCode2 = Encoding.UTF8.GetString(mfaSetup.RecoveryCode2),
            RecoveryCode3 = Encoding.UTF8.GetString(mfaSetup.RecoveryCode3),
        };
    }

    public async Task<SetupMFAResponse> ResetMFA(ResetMFARequest request, Account account, CancellationToken cancellationToken = default)
    {
        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id);
        
        if (mfaSetup == null)
            throw new BusinessException("MFA setup not found");
        
        if (mfaSetup.Status != MfaSetupStatus.Confirmed)
        {
            throw new BusinessException("MFA not confirmed");
        }
        
        if (request.RecoveryCode1 != Encoding.UTF8.GetString(mfaSetup.RecoveryCode1)
            && request.RecoveryCode2 != Encoding.UTF8.GetString(mfaSetup.RecoveryCode2)
            && request.RecoveryCode3 != Encoding.UTF8.GetString(mfaSetup.RecoveryCode3))
        {
            throw new BusinessException("Invalid recovery codes");
        }
        
        await _mfaSetupRepository.DeleteAsync(mfaSetup.Id);
        
        return await SetupMFA(account, cancellationToken);
    }
}
