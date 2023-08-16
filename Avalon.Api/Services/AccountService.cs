using System.Net;
using System.Text;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Database.Auth;

namespace Avalon.Api.Services;

public interface IAccountService
{
    Task<Account?> FindById(int id);
    Task<string?> Authenticate(AuthenticateRequest model, IPAddress ipAddress, CancellationToken cancellationToken);
    Task<string> Register(RegisterRequest model, IPAddress ipAddress, CancellationToken cancellationToken);
}

public class AccountService : IAccountService
{
    private readonly IAuthDatabase _authDatabase;
    private readonly IJwtUtils _jwtUtils;

    public AccountService(IAuthDatabase authDatabase, IJwtUtils jwtUtils)
    {
        _authDatabase = authDatabase;
        _jwtUtils = jwtUtils;
    }

    public async Task<Account?> FindById(int id)
    {
        return await _authDatabase.Account.QueryByIdAsync(id);
    }

    public async Task<string?> Authenticate(AuthenticateRequest model, IPAddress ipAddress, 
        CancellationToken cancellationToken)
    {
        var account = await _authDatabase.Account.QueryByUsernameAsync(model.Username);
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

        var inserted = await _authDatabase.Account.InsertAccountAsync(model.Username, saltBytes, hashBytes);
        
        if (!inserted)
            throw new Exception("Failed to insert account");
        
        var account = await _authDatabase.Account.QueryByUsernameAsync(model.Username);
        if (account == null)
            throw new Exception("Failed to fetch newly registered account");

        return _jwtUtils.GenerateJwtToken(account);
    }
}
