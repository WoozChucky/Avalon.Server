using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Party;

/// <summary>Sets how the party shares experience; leader only. Answered with SMSG_PARTY_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_PARTY_EXPERIENCE_MODE)]
public class CPartyExperienceModePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PARTY_EXPERIENCE_MODE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public PartyExperienceMode Mode { get; set; }
}
