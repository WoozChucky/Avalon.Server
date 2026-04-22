using System.Net;
using System.Security.Authentication;
using System.Text;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Services;
using OperatingSystem = Avalon.Domain.Auth.OperatingSystem;

namespace Avalon.Api.Services;

public interface IAccountService
{
    Task<Account?> FindByIdAsync(AccountId id, CancellationToken cancellationToken = default);
    Task<AuthenticateResponse> Authenticate(AuthenticateRequest model, IPAddress ipAddress, CancellationToken cancellationToken);
    Task<RegisterResponse> Register(RegisterRequest model, string userAgent, IPAddress ipAddress,
        CancellationToken cancellationToken);

    Task<PagedResult<Account>> Paginate(AccountPaginateFilters filters, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(AccountId accountId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<string> InitiateEmailChangeAsync(AccountId accountId, string newEmail, CancellationToken cancellationToken = default);
    Task ConfirmEmailChangeAsync(string token, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(AccountId accountId, AccountStatus state, string? reason, AccountId actorId, CancellationToken cancellationToken = default);
    Task UpdateRolesAsync(AccountId accountId, AccountAccessLevel roles, CancellationToken cancellationToken = default);
}

public class AccountService : IAccountService
{
    private readonly ILogger<AccountService> _logger;
    private readonly IAccountRepository _accountRepository;
    private readonly IJwtUtils _jwtUtils;
    private readonly IMFAHashService _mfaHashService;
    private readonly IMfaSetupRepository _mfaSetupRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IReplicatedCache _cache;
    private readonly ISecureRandom _secureRandom;
    private readonly IPersonalAccessTokenService _patService;
    private readonly AuthDbContext _authDbContext;
    public AccountService(ILoggerFactory loggerFactory,
        IAccountRepository accountRepository,
        IJwtUtils jwtUtils,
        IMFAHashService mfaHashService,
        IMfaSetupRepository mfaSetupRepository,
        IDeviceRepository deviceRepository,
        IReplicatedCache cache,
        ISecureRandom secureRandom,
        IPersonalAccessTokenService patService,
        AuthDbContext authDbContext)
    {
        _logger = loggerFactory.CreateLogger<AccountService>();
        _accountRepository = accountRepository;
        _jwtUtils = jwtUtils;
        _mfaHashService = mfaHashService;
        _mfaSetupRepository = mfaSetupRepository;
        _deviceRepository = deviceRepository;
        _cache = cache;
        _secureRandom = secureRandom;
        _patService = patService;
        _authDbContext = authDbContext;
    }

    public async Task<Account?> FindByIdAsync(AccountId id, CancellationToken cancellationToken = default)
    {
        return await _accountRepository.FindByIdAsync(id, track: false, cancellationToken);
    }

    public async Task<AuthenticateResponse> Authenticate(AuthenticateRequest model, IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        var account = await _accountRepository.FindByUserNameAsync(model.Username.ToUpperInvariant().Trim(), cancellationToken);
        if (account == null)
            throw new AuthenticationException("Invalid username or password");

        var hash = Encoding.UTF8.GetString(account.Verifier);

        if (!BCrypt.Net.BCrypt.Verify(model.Password, hash))
        {
            throw new AuthenticationException("Invalid username or password");
        }

        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id, cancellationToken);
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

        await _accountRepository.UpdateAsync(account, cancellationToken);

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
        var existingAccount = await _accountRepository.FindByUserNameAsync(
            model.Username.ToUpperInvariant().Trim(),
            cancellationToken
        );
        if (existingAccount != null)
            throw new BusinessException("Username already exists");

        existingAccount = await _accountRepository.FindByEmailAsync(model.Email, cancellationToken);
        if (existingAccount != null)
            throw new BusinessException("Email already exists");

        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var hash = BCrypt.Net.BCrypt.HashPassword(model.Password.Trim(), salt);

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hashBytes = Encoding.UTF8.GetBytes(hash);

        var account = new Account
        {
            Username = model.Username.ToUpperInvariant().Trim(),
            Email = model.Email,
            Salt = saltBytes,
            Verifier = hashBytes,
            LastIp = ipAddress.ToString(),
            LastLogin = DateTime.UtcNow,
            JoinDate = DateTime.UtcNow,
            Locale = AccountLocale.enUS,
            Os = OperatingSystem.Windows,
        };

        account = await _accountRepository.CreateAsync(account, cancellationToken);

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
        }, cancellationToken);

        return new RegisterResponse
        {
            Token = _jwtUtils.GenerateJwtToken(account),
            RefreshToken = "",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        };
    }

    public async Task<PagedResult<Account>> Paginate(AccountPaginateFilters filters, CancellationToken cancellationToken)
    {
        return await _accountRepository.PaginateAsync(filters, false, cancellationToken);
    }

    public async Task ChangePasswordAsync(AccountId accountId, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.FindByIdAsync(accountId, track: true, cancellationToken);
        if (account == null)
            throw new AuthenticationException("Invalid current password");

        var existingHash = Encoding.UTF8.GetString(account.Verifier);
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, existingHash))
        {
            throw new AuthenticationException("Invalid current password");
        }

        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var hash = BCrypt.Net.BCrypt.HashPassword(newPassword.Trim(), salt);

        account.Salt = Encoding.UTF8.GetBytes(salt);
        account.Verifier = Encoding.UTF8.GetBytes(hash);

        await _accountRepository.UpdateAsync(account, cancellationToken);
    }

    public async Task<string> InitiateEmailChangeAsync(AccountId accountId, string newEmail,
        CancellationToken cancellationToken = default)
    {
        var raw = _secureRandom.GetBytes(24);
        var token = Convert.ToBase64String(raw).Replace("+", "-").Replace("/", "_").TrimEnd('=');

        var payload = $"{accountId.Value}|{newEmail}";
        var key = $"auth:emailChange:{token}";
        await _cache.SetAsync(key, payload, TimeSpan.FromMinutes(15));

        return token;
    }

    public async Task ConfirmEmailChangeAsync(string token, CancellationToken cancellationToken = default)
    {
        var key = $"auth:emailChange:{token}";
        var payload = await _cache.GetAsync(key)
            ?? throw new BusinessException("Invalid or expired token");
        await _cache.RemoveAsync(key);

        var parts = payload.Split('|', 2);
        if (parts.Length != 2)
            throw new BusinessException("Invalid token payload");

        var accountId = new AccountId(long.Parse(parts[0]));
        var newEmail = parts[1];

        var account = await _accountRepository.FindByIdAsync(accountId, track: true, cancellationToken)
            ?? throw new BusinessException("Account not found");

        account.Email = newEmail;
        await _accountRepository.UpdateAsync(account, cancellationToken);
    }

    // NOTE: `reason` is currently accepted but not persisted (future: audit log).
    public async Task UpdateStatusAsync(AccountId accountId, AccountStatus state, string? reason,
        AccountId actorId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _authDbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var account = await _accountRepository.FindByIdAsync(accountId, track: true, cancellationToken)
                ?? throw new BusinessException("Account not found");

            account.Status = state;
            await _accountRepository.UpdateAsync(account, cancellationToken);

            if (state is AccountStatus.Banned or AccountStatus.Deactivated)
            {
                await _patService.RevokeAllForAccountAsync(accountId, actorId, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        if (state is AccountStatus.Banned or AccountStatus.Deactivated)
        {
            await _cache.PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, accountId.Value.ToString());
        }
    }

    public async Task UpdateRolesAsync(AccountId accountId, AccountAccessLevel roles, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.FindByIdAsync(accountId, track: true, cancellationToken)
            ?? throw new BusinessException("Account not found");
        account.AccessLevel = roles;
        await _accountRepository.UpdateAsync(account, cancellationToken);
    }
}
