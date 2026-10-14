using Avalon.Common.Cryptography;
using Avalon.Common.GameAuth;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure.GameAuth;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Serialization;
using Avalon.World;
using Avalon.World.GameAuth;
using Avalon.World.Persistence;

namespace Avalon.Server.World.Handlers;

/// <summary>
/// Redeems a join ticket and admits the connection. On a world that seals (<c>Network:PacketEncryption</c>), admission
/// also completes the key exchange: the client's public key must be one, the session is keyed from it, and the reply
/// carries the connection's own. A world that does not seal runs no exchange (#878): whatever public key the client
/// sends (a 0.3.0 client sends one before it knows the mode) is ignored, and the reply's key is empty.
/// </summary>
/// <remarks>
/// The session is keyed off the tick, after the ticket is redeemed (an agreement, two derivations and two ciphers, about
/// 0.14 ms); the tick only publishes the admission and sends the reply. Keying first is safe: the read loop opens nothing
/// with the session until it reads Ready, which is written after both ciphers; the reply that carries the server's key,
/// the only way a client can seal, goes out after the keying has finished; and a packet that arrives before the
/// admission is published is dropped, since the connection has no lease yet.
/// </remarks>
public sealed class GameAdmissionHandler(IGameAdmissionClient admission) : IWorldPacketHandler<CGameAdmissionPacket>
{
    public Task ExecuteAsync(WorldPacketContext<CGameAdmissionPacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection is not WorldConnection connection || !connection.IsTlsAuthenticated ||
            !GameAuthCryptography.IsToken(ctx.Packet.JoinTicket) ||
            (connection.KeyExchange is { } exchange && ctx.Packet.PublicKey.Length != exchange.ServerCrypto.GetValidKeySize()) ||
            !connection.TryBeginAdmission())
        {
            ctx.Connection.Send(SGameAdmissionPacket.Create([], PacketEncoder.Shared, GameAdmissionResult.InvalidRequest));
            return ctx.Connection.CloseAsync(false);
        }
        ConnectionKeyExchange? keys = connection.KeyExchange;
        byte[] clientKey = keys is null ? [] : ctx.Packet.PublicKey.ToArray();
        Task<WorldAdmissionResult> redeemed = WorldDatabaseWork.Admission.Run(() => admission.AdmitAsync(ctx.Packet.JoinTicket, connection.Id, Guid.NewGuid(), token));
        connection.TrackAdmission(redeemed);
        connection.EnqueueContinuation(KeyAsync(redeemed, keys, clientKey), outcome =>
        {
            WorldAdmissionResult reply = outcome.Reply;
            if (!connection.IsConnected || connection.IsClosing) return;
            if (reply.Lease is null || !reply.Lease.IsActive)
            {
                connection.Send(SGameAdmissionPacket.Create([], PacketEncoder.Shared, reply.Error == GameAuthErrors.ServiceUnavailable || reply.Error == GameAuthErrors.BarrierPending
                    ? GameAdmissionResult.ServiceUnavailable : GameAdmissionResult.AuthorizationRequired));
#pragma warning disable MA0045 // Tick continuations must not await socket cleanup.
                connection.Close(false);
#pragma warning restore MA0045
                return;
            }
            if (outcome.Keying != Keying.Done)
            {
                if (outcome.Keying == Keying.BadClientKey)
                {
                    connection.ReportBadPublicKey(reply.Lease.Authority.AccountId.Value);
                    connection.Send(SGameAdmissionPacket.Create([], PacketEncoder.Shared, GameAdmissionResult.InvalidRequest));
                }
                else
                {
                    connection.ReportKeyingFailed(outcome.Error!);
                }
                reply.Lease.Revoke();
#pragma warning disable MA0045 // Tick continuations must not await socket cleanup.
                connection.Close(false);
#pragma warning restore MA0045
                return;
            }
            try
            {
                connection.PublishAdmission(reply.Lease);
                connection.Send(SGameAdmissionPacket.Create(keys?.ServerCrypto.GetPublicKey() ?? [], PacketEncoder.Shared,
                    GameAdmissionResult.Accepted, connection.PacketEncryption));
            }
            catch (Exception error)
            {
                connection.ReportAdmissionFailed(error);
                reply.Lease.Revoke();
#pragma warning disable MA0045 // Tick continuations must not await socket cleanup.
                connection.Close(false);
#pragma warning restore MA0045
            }
        });
        return Task.CompletedTask;
    }

    private enum Keying { Done, BadClientKey, Failed }

    private readonly record struct KeyedAdmission(WorldAdmissionResult Reply, Keying Keying, Exception? Error);

    /// <summary>
    /// Keys the session, on a world that seals, once the ticket is redeemed for an active lease: off the tick, on the
    /// thread the redemption finished on. Never faults for the keying: the redemption's own task, which the connection
    /// tracks so a drain can end a lease that was never published, stays the one that says what was redeemed.
    /// </summary>
    private static async Task<KeyedAdmission> KeyAsync(Task<WorldAdmissionResult> redeemed, ConnectionKeyExchange? keys,
        byte[] clientKey)
    {
        WorldAdmissionResult reply = await redeemed.ConfigureAwait(false);
        if (keys is null || reply.Lease is not { IsActive: true })
            return new KeyedAdmission(reply, Keying.Done, null);
        try
        {
            keys.Session.Initialize(clientKey);
            return new KeyedAdmission(reply, Keying.Done, null);
        }
        catch (InvalidPublicKeyException)
        {
            return new KeyedAdmission(reply, Keying.BadClientKey, null);
        }
        catch (Exception error)
        {
            return new KeyedAdmission(reply, Keying.Failed, error);
        }
    }
}
