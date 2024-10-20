using System.Net;
using System.Security.Authentication;
using System.Text;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Services;
using OperatingSystem = Avalon.Domain.Auth.OperatingSystem;

namespace Avalon.Api.Services;

public interface IAccountService
{
    Task<Account?> FindById(AccountId id);
    Task<AuthenticateResponse> Authenticate(AuthenticateRequest model, IPAddress ipAddress, CancellationToken cancellationToken);
    Task<RegisterResponse> Register(RegisterRequest model, string userAgent, IPAddress ipAddress,
        CancellationToken cancellationToken);
}

public class AccountService : IAccountService
{
    private readonly ILogger<AccountService> _logger;
    private readonly IAccountRepository _accountRepository;
    private readonly IJwtUtils _jwtUtils;
    private readonly IMFAHashService _mfaHashService;
    private readonly IMfaSetupRepository _mfaSetupRepository;
    private readonly IDeviceRepository _deviceRepository;
    public AccountService(ILoggerFactory loggerFactory,
        IAccountRepository accountRepository,
        IJwtUtils jwtUtils,
        IMFAHashService mfaHashService,
        IMfaSetupRepository mfaSetupRepository,
        IDeviceRepository deviceRepository)
    {
        _logger = loggerFactory.CreateLogger<AccountService>();
        _accountRepository = accountRepository;
        _jwtUtils = jwtUtils;
        _mfaHashService = mfaHashService;
        _mfaSetupRepository = mfaSetupRepository;
        _deviceRepository = deviceRepository;
    }

    public async Task<Account?> FindById(AccountId id)
    {
        return await _accountRepository.FindByIdAsync(id);
    }

    public async Task<AuthenticateResponse> Authenticate(AuthenticateRequest model, IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        var account = await _accountRepository.FindByUserNameAsync(model.Username);
        if (account == null)
            throw new AuthenticationException("Invalid username or password");

        var hash = Encoding.UTF8.GetString(account.Verifier);

        if (!BCrypt.Net.BCrypt.Verify(model.Password, hash))
        {
            throw new AuthenticationException("Invalid username or password");
        }

        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id);
        if (mfaSetup is { Status: MfaSetupStatus.Confirmed })
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

        await _accountRepository.UpdateAsync(account);

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
        var existingAccount = await _accountRepository.FindByUserNameAsync(model.Username);
        if (existingAccount != null)
            throw new BusinessException("Username already exists");

        existingAccount = await _accountRepository.FindByEmailAsync(model.Email);
        if (existingAccount != null)
            throw new BusinessException("Email already exists");

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
            Locale = AccountLocale.enUS,
            Os = OperatingSystem.Windows,
        };

        account = await _accountRepository.CreateAsync(account);

        if (account == null)
            throw new Exception("Failed to insert account");

        await _deviceRepository.CreateAsync(new Device
        {
            Account = account,
            AccountId = account.Id,
            Name = userAgent,
            LastUsage = DateTime.UtcNow,
            Trusted = false,
            TrustEnd = DateTime.UtcNow,
        });

        return new RegisterResponse
        {
            Token = _jwtUtils.GenerateJwtToken(account),
            RefreshToken = "",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        };
    }
}
