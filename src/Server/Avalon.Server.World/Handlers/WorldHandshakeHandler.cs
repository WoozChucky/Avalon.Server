using Avalon.Common.Utils;
using Avalon.Network.Packets.Auth;
using Avalon.World;

namespace Avalon.Server.World.Handlers;

public sealed class WorldHandshakeHandler(IWorld world) : IWorldPacketHandler<CWorldHandshakePacket>
{
    public Task ExecuteAsync(WorldPacketContext<CWorldHandshakePacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection is not WorldConnection connection || connection.GameSessionLease?.IsActive != true ||
            !SemVerPacker.TryPack(ctx.Packet.Version, out uint version) || version < SemVerPacker.Pack("0.2.0") ||
            !SemVerPacker.TryPack(world.MinVersion, out uint minimum) || version < minimum || !connection.AcceptProtocol())
        {
            return ctx.Connection.CloseAsync(false);
        }

        connection.Send(SWorldHandshakePacket.Create(connection.AccountId!, true, connection.CryptoSession.Encryptor));
        connection.RequestInitialTimeSyncPing();
        return Task.CompletedTask;
    }
}
