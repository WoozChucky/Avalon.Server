using System.Net;
using System.Security.Authentication;
using System.Text;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Database.Auth;
using Avalon.Domain.Auth;

namespace Avalon.Api.Services;

public interface IAccountService
{
    Task<Account?> FindById(int id);
    Task<AuthenticateResponse> Authenticate(AuthenticateRequest model, IPAddress ipAddress, CancellationToken cancellationToken);
    Task<RegisterResponse> Register(RegisterRequest model, string userAgent, IPAddress ipAddress,
        CancellationToken cancellationToken);
}

public class AccountService : IAccountService
{
    private readonly ILogger<AccountService> _logger;
    private readonly IAccountRepository _authRepository;
    private readonly IJwtUtils _jwtUtils;
    private readonly IMFAHashService _mfaHashService;
    private readonly IMFASetupRepository _mfaSetupRepository;
    private readonly IDeviceRepository _deviceRepository;
    public AccountService(ILoggerFactory loggerFactory, 
        IAccountRepository authRepository, 
        IJwtUtils jwtUtils, 
        IMFAHashService mfaHashService, 
        IMFASetupRepository mfaSetupRepository, 
        IDeviceRepository deviceRepository)
    {
        _logger = loggerFactory.CreateLogger<AccountService>();
        _authRepository = authRepository;
        _jwtUtils = jwtUtils;
        _mfaHashService = mfaHashService;
        _mfaSetupRepository = mfaSetupRepository;
        _deviceRepository = deviceRepository;
    }
    
    public async Task<Account?> FindById(int id)
    {
        return await _authRepository.FindByIdAsync(id);
    }

    public async Task<AuthenticateResponse> Authenticate(AuthenticateRequest model, IPAddress ipAddress, 
        CancellationToken cancellationToken)
    {
        var account = await _authRepository.FindByUsernameAsync(model.Username);
        if (account == null)
            throw new AuthenticationException("Invalid username or password");

        var hash = Encoding.UTF8.GetString(account.Verifier);

        if (!BCrypt.Net.BCrypt.Verify(model.Password, hash))
        {
            throw new AuthenticationException("Invalid username or password");
        }
        
        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id!.Value);
        if (mfaSetup is { Status: Status.Confirmed } )
        {
            return new AuthenticateResponse
            {
                Token = null,
                ExpiresAt = null,
                MfaHash = await _mfaHashService.GenerateHashAsync(account),
                Status = AuthenticationResponseStatus.RequiresMFA
            };
        }
        
        account.LastIp = ipAddress.ToString();
        account.LastLogin = DateTime.UtcNow;
        
        await _authRepository.UpdateAsync(account);

        return new AuthenticateResponse
        {
            Token = _jwtUtils.GenerateJwtToken(account),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            Status = AuthenticationResponseStatus.Success
        };
    }

    public async Task<RegisterResponse> Register(RegisterRequest model, string userAgent, IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var hash = BCrypt.Net.BCrypt.HashPassword(model.Password.Trim(), salt);

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hashBytes = Encoding.UTF8.GetBytes(hash);

        var account = new Account
        {
            Username = model.Username,
            Email = model.Email,
            Salt = saltBytes,
            Verifier = hashBytes,
            LastIp = ipAddress.ToString(),
            LastLogin = DateTime.UtcNow,
            JoinDate = DateTime.UtcNow,
        };
        
        account = await _authRepository.SaveAsync(account);
        
        if (account == null)
            throw new Exception("Failed to insert account");
        
        await _deviceRepository.SaveAsync(new Device
        {
            AccountId = account.Id!.Value,
            Name = userAgent,
            LastUsage = DateTime.UtcNow,
            Trusted = false,
            TrustEnd = DateTime.UtcNow,
        });

        return new RegisterResponse
        {
            Token = _jwtUtils.GenerateJwtToken(account),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        };
    }
}
