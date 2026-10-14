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
        Task<WorldAdmissionResult> result = WorldDatabaseWork.Admission.Run(() => admission.AdmitAsync(ctx.Packet.JoinTicket, connection.Id, Guid.NewGuid(), token));
        connection.TrackAdmission(result);
        connection.EnqueueContinuation(result, reply =>
        {
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
            try
            {
                keys?.Session.Initialize(clientKey);
                connection.PublishAdmission(reply.Lease);
                connection.Send(SGameAdmissionPacket.Create(keys?.ServerCrypto.GetPublicKey() ?? [], PacketEncoder.Shared,
                    GameAdmissionResult.Accepted, connection.PacketEncryption));
            }
            catch (Exception)
            {
                reply.Lease.Revoke();
#pragma warning disable MA0045 // Tick continuations must not await socket cleanup.
                connection.Close(false);
#pragma warning restore MA0045
            }
        });
        return Task.CompletedTask;
    }
}
