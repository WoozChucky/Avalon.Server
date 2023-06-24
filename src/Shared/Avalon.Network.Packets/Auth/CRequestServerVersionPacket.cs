using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class CRequestServerVersionPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_REQUEST_SERVER_VERSION;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
}
