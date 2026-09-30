using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Party;

/// <summary>Makes another member the party leader; leader only. Answered with SMSG_PARTY_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_PARTY_PROMOTE)]
public class CPartyPromotePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PARTY_PROMOTE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint CharacterId { get; set; }
}
