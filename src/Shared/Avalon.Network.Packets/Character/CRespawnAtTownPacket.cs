using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class CRespawnAtTownPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_RESPAWN_AT_TOWN;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
}
