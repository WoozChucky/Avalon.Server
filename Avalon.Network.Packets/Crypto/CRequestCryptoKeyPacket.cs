using ProtoBuf;

namespace Avalon.Network.Packets.Crypto;

[ProtoContract]
public class CRequestCryptoKeyPacket
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY;
}
