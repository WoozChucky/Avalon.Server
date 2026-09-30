using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Party;

/// <summary>Accepts or declines the party invite the sender holds. Answered with SMSG_PARTY_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_PARTY_INVITE_RESPONSE)]
public class CPartyInviteResponsePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PARTY_INVITE_RESPONSE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public bool Accept { get; set; }
}
