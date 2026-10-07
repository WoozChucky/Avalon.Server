using Avalon.Common.Utils;
using Avalon.Network.Packets.Auth;
using Avalon.World;

namespace Avalon.Server.World.Handlers;

// The unread logger stays: the code standard does not change the constructor ActivatorUtilities builds (#791).
#pragma warning disable CS9113
public sealed class WorldHandshakeHandler(Microsoft.Extensions.Logging.ILogger<WorldHandshakeHandler> logger, IWorld world) : IWorldPacketHandler<CWorldHandshakePacket>
#pragma warning restore CS9113
{
    public Task ExecuteAsync(WorldPacketContext<CWorldHandshakePacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection is not WorldConnection connection || connection.GameSessionLease?.IsActive != true ||
            !SemVerPacker.TryPack(ctx.Packet.Version, out uint version) || version < SemVerPacker.Pack("0.2.0") ||
            !SemVerPacker.TryPack(world.MinVersion, out uint minimum) || version < minimum || !connection.AcceptProtocol())
        {
            return ctx.Connection.CloseAsync(false);
        }

        connection.Send(SWorldHandshakePacket.Create(connection.AccountId!, true, connection.CryptoSession.Encrypt));
        connection.RequestInitialTimeSyncPing();
        return Task.CompletedTask;
    }
}
