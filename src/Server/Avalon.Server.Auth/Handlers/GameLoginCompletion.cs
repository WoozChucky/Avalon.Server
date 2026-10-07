using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Auth;

namespace Avalon.Server.Auth.Handlers;

internal static class GameLoginCompletion
{
    public static async Task<AuthResult?> TryStartAsync(IAuthConnection connection, Account account, string lastIp,
        AuthResult guardFailure, IAccountRepository accounts, IReplicatedCache cache, ILogger logger,
        CancellationToken token, bool ticket = false)
    {
        if (account.Online)
        {
            connection.Send(SAuthResultPacket.Create(null, null, AuthResult.ALREADY_CONNECTED,
                connection.CryptoSession.Encrypt));
            connection.Server?.NoteOwnDisconnectPublish(account.Id);
            await cache.PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, account.Id.ToString());

            // Server is declared non-null; only the ?. above makes the flow analysis doubt it.
#pragma warning disable CS8602
            IAuthConnection? connectedSession = connection.Server.Connections.FirstOrDefault(c => c.AccountId == account.Id);
#pragma warning restore CS8602
            if (connectedSession != null)
            {
                connectedSession.Close();
            }
            else
            {
                logger.LogWarning("Account {AccountId} is online but no connection was found", account.Id);
                account.Online = false;
                await accounts.MarkOfflineAsync(account.Id, account.OnlineSessionId, cancellationToken: token);
            }
            return AuthResult.ALREADY_CONNECTED;
        }

        int credentialsVersion = account.CredentialsVersion;
        bool recorded = ticket
            ? await accounts.TryRecordTicketLoginAsync(account.Id, credentialsVersion, lastIp, DateTime.UtcNow,
                connection.Id, token)
            : await accounts.TryRecordLoginAsync(account.Id, lastIp, DateTime.UtcNow, connection.Id, token);
        if (!recorded)
        {
            AuthResult refusal = ticket ? AuthResult.INVALID_CREDENTIALS : guardFailure;
            if (ticket)
            {
                // The guarded write can lose to a ban, revocation, lock or another login.
                // A winner of the login race must not be kicked by this losing attempt.
                Account? current = await accounts.FindByIdAsync(account.Id, false, token);
                if (current is null || current.CredentialsVersion != credentialsVersion ||
                    !AccessLevels.Player.Allows(current.AccessLevel))
                {
                    refusal = AuthResult.INVALID_CREDENTIALS;
                }
                else if (current.Status != AccountStatus.Active)
                {
                    refusal = current.Status == AccountStatus.Deactivated ? AuthResult.DEACTIVATED : AuthResult.BANNED;
                }
                else if (current.Online)
                {
                    refusal = AuthResult.ALREADY_CONNECTED;
                }
                else if (current.IsLockedAt(DateTime.UtcNow))
                {
                    refusal = AuthResult.LOCKED;
                }
            }
            logger.LogWarning("Account {AccountId} could not claim a game login", account.Id);
            connection.Send(SAuthResultPacket.Create(null, null, refusal, connection.CryptoSession.Encrypt));
            return refusal;
        }
        return null;
    }

    public static async Task FinishAsync(IAuthConnection connection, Account account, string lastIp,
        IReplicatedCache cache, bool ticket = false)
    {
        connection.CredentialsVersion = account.CredentialsVersion;
        connection.LoggedInAt = System.Diagnostics.Stopwatch.GetTimestamp();
        connection.AccountId = account.Id;

        account.Online = true;
        account.LastIp = lastIp;
        account.LastLogin = DateTime.UtcNow;
        if (!ticket) account.FailedLogins = 0;
        account.Locked = false;
        account.LockedUntil = null;

        await cache.PublishAsync(CacheKeys.AuthAccountsOnlineChannel, account.Id.ToString());
        connection.Send(SAuthResultPacket.Create(account.Id, null, AuthResult.SUCCESS,
            connection.CryptoSession.Encrypt));
    }
}
