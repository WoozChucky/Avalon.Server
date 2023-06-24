using ProtoBuf;

namespace Avalon.Network.Packets.Crypto;

[ProtoContract]
public class CRequestCryptoKeyPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
}
