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

            var connectedSession = connection.Server.Connections.FirstOrDefault(c => c.AccountId == account.Id);
            if (connectedSession != null)
                connectedSession.Close();
            else
            {
                logger.LogWarning("Account {AccountId} is online but no connection was found", account.Id);
                account.Online = false;
                await accounts.MarkOfflineAsync(account.Id, account.OnlineSessionId, cancellationToken: token);
            }
            return AuthResult.ALREADY_CONNECTED;
        }

        bool recorded = ticket
            ? await accounts.TryRecordTicketLoginAsync(account.Id, account.CredentialsVersion, lastIp, DateTime.UtcNow,
                connection.Id, token)
            : await accounts.TryRecordLoginAsync(account.Id, lastIp, DateTime.UtcNow, connection.Id, token);
        if (!recorded)
        {
            AuthResult refusal = guardFailure;
            if (ticket && (await accounts.FindByIdAsync(account.Id, false, token))?.Online == true)
                refusal = AuthResult.ALREADY_CONNECTED;
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
