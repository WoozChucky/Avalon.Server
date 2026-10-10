using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Party;

/// <summary>Someone invites this player to a party. Answered with CMSG_PARTY_INVITE_RESPONSE before it expires.</summary>
[ProtoContract]
public class SPartyInvitePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PARTY_INVITE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public string InviterName { get; set; } = string.Empty;
    [ProtoMember(2)] public ushort InviterClass { get; set; }
    [ProtoMember(3)] public ushort InviterLevel { get; set; }
    [ProtoMember(4)] public uint ExpiresInMs { get; set; }

    public static OutboundPacket Create(string inviterName, ushort inviterClass, ushort inviterLevel, uint expiresInMs,
        PacketEncoder encoder)
    {
        SPartyInvitePacket message = PacketEncoder.Scratch<SPartyInvitePacket>();
        message.InviterName = inviterName;
        message.InviterClass = inviterClass;
        message.InviterLevel = inviterLevel;
        message.ExpiresInMs = expiresInMs;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
