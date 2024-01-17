using System.Net;
using System.Security.Authentication;
using System.Text;
using Avalon.Api.Authentication;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Database.Auth;
using Avalon.Domain.Auth;
using Avalon.Repositories;
using OtpNet;

namespace Avalon.Api.Services;

public interface IAccountService
{
    Task<Account?> FindById(int id);
    Task<AuthenticateResponse> Authenticate(AuthenticateRequest model, IPAddress ipAddress, CancellationToken cancellationToken);
    Task<RegisterResponse> Register(RegisterRequest model, IPAddress ipAddress, CancellationToken cancellationToken);
    Task<Setup2FAResponse> Setup2FA(Account account, CancellationToken cancellationToken);
}

public class AccountService(IAccountRepository authRepository, IJwtUtils jwtUtils, ApplicationConfig applicationConfig) : IAccountService
{
    public async Task<Account?> FindById(int id)
    {
        return await authRepository.FindByIdAsync(id);
    }

    public async Task<AuthenticateResponse> Authenticate(AuthenticateRequest model, IPAddress ipAddress, 
        CancellationToken cancellationToken)
    {
        var account = await authRepository.FindByUsernameAsync(model.Username);
        if (account == null)
            throw new AuthenticationException("Invalid username or password");

        var hash = Encoding.UTF8.GetString(account.Verifier);

        if (!BCrypt.Net.BCrypt.Verify(model.Password, hash))
        {
            throw new AuthenticationException("Invalid username or password");
        }

        return new AuthenticateResponse
        {
            Token = jwtUtils.GenerateJwtToken(account),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        };
    }

    public async Task<RegisterResponse> Register(RegisterRequest model, IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var hash = BCrypt.Net.BCrypt.HashPassword(model.Password.Trim(), salt);

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hashBytes = Encoding.UTF8.GetBytes(hash);
        
        var totpSecret = KeyGeneration.GenerateRandomKey(32);

        var account = new Account
        {
            Username = model.Username,
            Email = model.Email,
            TotpSecret = totpSecret,
            Salt = saltBytes,
            Verifier = hashBytes,
            LastIp = ipAddress.ToString(),
        };
        
        var inserted = await authRepository.SaveAsync(account);
        
        if (inserted == null)
            throw new Exception("Failed to insert account");

        return new RegisterResponse
        {
            Token = jwtUtils.GenerateJwtToken(inserted),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        };
    }

    public async Task<Setup2FAResponse> Setup2FA(Account account, CancellationToken cancellationToken)
    {
        var totp = new Totp(account.TotpSecret);
        var uriString = new OtpUri(OtpType.Totp, account.TotpSecret, account.Email, applicationConfig.Authentication.Issuer).ToString();
        
        return new Setup2FAResponse
        {
            SecretKey = Encoding.UTF8.GetString(account.TotpSecret),
            Uri = uriString
        };
    }
}
