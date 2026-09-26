using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;

namespace Avalon.Server.Auth.Handlers;

/// <summary>
/// The check every auth-server handler that needs a logged-in connection runs first (#495 review):
/// MFA setup, confirm and reset, the world list, world select. The account is read fresh, and the
/// connection is closed, and null returned, when the connection is not logged in, the account is
/// gone, it is no longer Active (banned or deactivated since the login), or its credentials
/// version has moved past the one this connection's login proved (a password change, an MFA reset
/// or an admin's MFA removal since). A session that fails any of these must not change MFA or get
/// a world key.
/// </summary>
public static class PostLoginGuard
{
    public static async Task<Account?> AccountOrCloseAsync(IAuthConnection connection, IAccountRepository accounts,
        ILogger logger, string action, CancellationToken token)
    {
        if (connection.AccountId == null)
        {
            logger.LogWarning("Unauthenticated connection attempted {Action} from {Endpoint}", action,
                connection.RemoteEndPoint);
            connection.Close();
            return null;
        }

        Account? account = await accounts.FindByIdAsync(connection.AccountId, false, token);
        if (account == null)
        {
            logger.LogWarning("Account not found for connection {Session} attempting {Action}", connection.Id, action);
            connection.Close();
            return null;
        }

        if (account.Status != AccountStatus.Active)
        {
            logger.LogWarning("Account {AccountId} attempted {Action} while {Status}", account.Id, action, account.Status);
            connection.Close();
            return null;
        }

        if (account.CredentialsVersion != connection.CredentialsVersion)
        {
            logger.LogWarning("Account {AccountId} attempted {Action} after its credentials changed", account.Id, action);
            connection.Close();
            return null;
        }

        return account;
    }
}
