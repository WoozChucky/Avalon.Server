using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_RESPAWN_AT_TOWN)]
public class CRespawnAtTownPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_RESPAWN_AT_TOWN;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
}
