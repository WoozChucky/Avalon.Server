using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Party;

/// <summary>Leaves the sender's party. Answered with SMSG_PARTY_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_PARTY_LEAVE)]
public class CPartyLeavePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PARTY_LEAVE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
}
