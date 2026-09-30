using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Party;

/// <summary>Removes a member from the party; leader only. Answered with SMSG_PARTY_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_PARTY_KICK)]
public class CPartyKickPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PARTY_KICK;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint CharacterId { get; set; }
}
