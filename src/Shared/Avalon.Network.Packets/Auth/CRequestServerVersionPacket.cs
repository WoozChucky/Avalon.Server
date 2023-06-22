using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class CRequestServerVersionPacket
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_REQUEST_SERVER_VERSION;
}
