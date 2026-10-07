using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World.Public;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_PONG)]
public class PongHandler : WorldPacketHandler<CPongPacket>
{
    public override void Execute(IWorldConnection connection, CPongPacket packet)
    {
        connection.OnPongReceived(packet.LastServerTimestamp, packet.ClientReceivedTimestamp, packet.ClientSentTimestamp,
            connection.CurrentPacketArrivedTicks);
    }
}

