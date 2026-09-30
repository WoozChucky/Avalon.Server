using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Party;

/// <summary>Invites a player, by character name, to the sender's party (or to a new one). Answered with SMSG_PARTY_RESULT.</summary>
[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_PARTY_INVITE)]
public class CPartyInvitePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PARTY_INVITE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string TargetName { get; set; } = string.Empty;
}
