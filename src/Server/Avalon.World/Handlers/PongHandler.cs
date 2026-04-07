using Avalon.World.Public;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_PONG)]
public class PongHandler : WorldPacketHandler<CPongPacket>
{
    public override void Execute(IWorldConnection connection, CPongPacket packet)
    {
        connection.OnPongReceived(packet.LastServerTimestamp, packet.ClientReceivedTimestamp, packet.ClientSentTimestamp);
    }
}

