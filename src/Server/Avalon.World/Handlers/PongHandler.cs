using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World.Public;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_PONG)]
public class PongHandler : WorldPacketHandler<CPongPacket>
{
    // The time sync is the world's own, on the concrete connection: not on IWorldConnection, the modding surface (#1034).
    public override void Execute(IWorldConnection connection, CPongPacket packet)
    {
        if (connection is WorldConnection world)
        {
            world.OnPongReceived(packet.LastServerTimestamp, packet.ClientReceivedTimestamp, packet.ClientSentTimestamp,
                connection.CurrentPacketArrivedTicks);
        }
    }
}
