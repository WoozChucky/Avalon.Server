using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Server.Auth.Handlers;

internal static class GameLoginCompletion
{
    public static async Task<AuthResult?> TryStartAsync(IAuthConnection connection, Account account, string lastIp,
        AuthResult guardFailure, IAccountRepository accounts, IReplicatedCache cache, ILogger logger,
        CancellationToken token)
    {
        if (account.Online)
        {
            connection.Send(SAuthResultPacket.Create(null, null, AuthResult.ALREADY_CONNECTED,
                PacketEncoder.Shared));
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

        if (!await accounts.TryRecordLoginAsync(account.Id, lastIp, DateTime.UtcNow, connection.Id, token))
        {
            logger.LogWarning("Account {AccountId} could not claim a game login", account.Id);
            connection.Send(SAuthResultPacket.Create(null, null, guardFailure, PacketEncoder.Shared));
            return guardFailure;
        }
        return null;
    }

    public static async Task FinishAsync(IAuthConnection connection, Account account, string lastIp,
        IReplicatedCache cache)
    {
        connection.CredentialsVersion = account.CredentialsVersion;
        connection.LoggedInAt = System.Diagnostics.Stopwatch.GetTimestamp();
        connection.AccountId = account.Id;

        account.Online = true;
        account.LastIp = lastIp;
        account.LastLogin = DateTime.UtcNow;
        account.FailedLogins = 0;
        account.Locked = false;
        account.LockedUntil = null;

        await cache.PublishAsync(CacheKeys.AuthAccountsOnlineChannel, account.Id.ToString());
        connection.Send(SAuthResultPacket.Create(account.Id, null, AuthResult.SUCCESS,
            PacketEncoder.Shared));
    }
}
