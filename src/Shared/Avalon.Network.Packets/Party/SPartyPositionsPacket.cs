using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Party;

/// <summary>Where one party member stands, on X/Z, for the recipient's map (#1199).</summary>
[ProtoContract]
public class PartyMemberPositionDto
{
    /// <summary>The member's character id, as the roster names it.</summary>
    [ProtoMember(1)] public uint CharacterId { get; set; }

    [ProtoMember(2)] public float X { get; set; }
    [ProtoMember(3)] public float Z { get; set; }
}

/// <summary>
/// The recipient's party members in its own instance that its world-state view does not hold (#1199): those beyond its
/// interest range, whole, about once a second, and at once on the tick a member's world-state remove is sent. A member
/// the view holds is never listed, nor is one in another instance; an empty list follows the last listed member's
/// leaving it. To the members of the party only.
/// </summary>
[ProtoContract]
public class SPartyPositionsPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PARTY_POSITIONS;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<PartyMemberPositionDto> Positions { get; set; } = [];

    /// <remarks>
    /// The list is the caller's, filled from entries it reuses, and is encoded before this returns, keeping nothing of
    /// it, so a warm send allocates nothing. An empty list is written as nothing, as a client reads an empty one.
    /// </remarks>
    public static OutboundPacket Create(List<PartyMemberPositionDto> positions, PacketEncoder encoder)
    {
        SPartyPositionsPacket message = PacketEncoder.Scratch<SPartyPositionsPacket>();
        message.Positions = positions;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
