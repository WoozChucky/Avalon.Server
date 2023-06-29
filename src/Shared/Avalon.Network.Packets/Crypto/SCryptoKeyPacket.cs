using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Crypto;

[ProtoContract]
public class SCryptoKeyPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_ENCRYPTION_KEY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    [ProtoMember(1)] public byte[] Key { get; set; }
}
