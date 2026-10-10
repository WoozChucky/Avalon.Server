using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Serialization;
using Avalon.World;
using Avalon.World.GameAuth;
using Avalon.World.Persistence;

namespace Avalon.Server.World.Handlers;

public sealed class GameAdmissionHandler(IGameAdmissionClient admission) : IWorldPacketHandler<CGameAdmissionPacket>
{
    public Task ExecuteAsync(WorldPacketContext<CGameAdmissionPacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection is not WorldConnection connection || !connection.IsTlsAuthenticated ||
            !GameAuthCryptography.IsToken(ctx.Packet.JoinTicket) || ctx.Packet.PublicKey.Length != connection.ServerCrypto.GetValidKeySize() ||
            !connection.TryBeginAdmission())
        {
            ctx.Connection.Send(SGameAdmissionPacket.Create([], PacketEncoder.Shared, GameAdmissionResult.InvalidRequest));
            return ctx.Connection.CloseAsync(false);
        }
        byte[] publicKey = ctx.Packet.PublicKey.ToArray();
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
                connection.CryptoSession.Initialize(publicKey);
                connection.PublishAdmission(reply.Lease);
                connection.Send(SGameAdmissionPacket.Create(connection.ServerCrypto.GetPublicKey(), PacketEncoder.Shared));
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
