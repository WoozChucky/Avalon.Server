using ProtoBuf;

namespace Avalon.Network.Packets.Crypto;

[ProtoContract]
public class SCryptoKeyPacket
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_ENCRYPTION_KEY;
    [ProtoMember(1)] public byte[] Key { get; set; }
}
