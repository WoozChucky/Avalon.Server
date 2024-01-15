using System.Net;
using System.Text;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Database.Auth;
using Avalon.Domain.Auth;
using Avalon.Repositories;
using OtpNet;

namespace Avalon.Api.Services;

public interface IAccountService
{
    Task<Account?> FindById(int id);
    Task<string?> Authenticate(AuthenticateRequest model, IPAddress ipAddress, CancellationToken cancellationToken);
    Task<string> Register(RegisterRequest model, IPAddress ipAddress, CancellationToken cancellationToken);
}

public class AccountService : IAccountService
{
    private readonly IAccountRepository _authRepository;
    private readonly IJwtUtils _jwtUtils;

    public AccountService(IAccountRepository authRepository, IJwtUtils jwtUtils)
    {
        _authRepository = authRepository;
        _jwtUtils = jwtUtils;
    }

    public async Task<Account?> FindById(int id)
    {
        return await _authRepository.FindByIdAsync(id);
    }

    public async Task<string?> Authenticate(AuthenticateRequest model, IPAddress ipAddress, 
        CancellationToken cancellationToken)
    {
        var account = await _authRepository.FindByUsernameAsync(model.Username);
        if (account == null)
            return null;

        var hash = Encoding.UTF8.GetString(account.Verifier);

        if (!BCrypt.Net.BCrypt.Verify(model.Password, hash))
        {
            throw new Exception("Invalid password");
        }
        
        return _jwtUtils.GenerateJwtToken(account);
    }

    public async Task<string> Register(RegisterRequest model, IPAddress ipAddress, CancellationToken cancellationToken)
    {
        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var hash = BCrypt.Net.BCrypt.HashPassword(model.Password.Trim(), salt);

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hashBytes = Encoding.UTF8.GetBytes(hash);
        
        var totpSecret = KeyGeneration.GenerateRandomKey(32);

        var account = new Account()
        {
            Username = model.Username,
            Email = model.Email,
            TotpSecret = totpSecret,
            Salt = saltBytes,
            Verifier = hashBytes,
            LastIp = ipAddress.ToString()
        };
        
        var inserted = await _authRepository.SaveAsync(account);
        
        if (inserted == null)
            throw new Exception("Failed to insert account");

        return _jwtUtils.GenerateJwtToken(inserted);
    }
}
