using Avalon.Domain.Auth;

namespace Avalon.Api.Authentication;

public interface IAuthContext
{
    Account? Account { get; }

    void Load(Account account);
}

public class AuthContext : IAuthContext
{
    public Account? Account { get; private set; }

    public void Load(Account account)
    {
        if (Account != null)
            throw new InvalidOperationException("Account already loaded");
        Account = account;
    }
}
