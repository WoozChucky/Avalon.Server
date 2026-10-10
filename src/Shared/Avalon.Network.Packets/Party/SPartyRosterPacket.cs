using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Party;

[ProtoContract]
public class PartyMemberDto
{
    [ProtoMember(1)] public uint CharacterId { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
    [ProtoMember(3)] public ushort Class { get; set; }
    [ProtoMember(4)] public ushort Level { get; set; }
    [ProtoMember(5)] public bool IsLeader { get; set; }
    [ProtoMember(6)] public bool Online { get; set; }

    /// <summary>In the recipient's instance. Always true for the recipient itself.</summary>
    [ProtoMember(7)] public bool SameInstance { get; set; }
}

/// <summary>
/// The whole party, as the recipient sees it, in join order (the first is the longest-standing). Sent on every
/// change of membership, leader, mode, online state or instance, on login and on instance entry. No members
/// (and PartyId 0) means the recipient is in no party.
/// </summary>
[ProtoContract]
public class SPartyRosterPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PARTY_ROSTER;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint PartyId { get; set; }
    [ProtoMember(2)] public PartyExperienceMode ExperienceMode { get; set; }

    /// <summary>How long until the leader may switch the mode again; 0 when it may now.</summary>
    [ProtoMember(3)] public uint ModeLockedForMs { get; set; }

    [ProtoMember(4)] public List<PartyMemberDto> Members { get; set; } = [];

    public static OutboundPacket Create(uint partyId, PartyExperienceMode mode, uint modeLockedForMs,
        List<PartyMemberDto> members, PacketEncoder encoder)
    {
        SPartyRosterPacket message = PacketEncoder.Scratch<SPartyRosterPacket>();
        message.PartyId = partyId;
        message.ExperienceMode = mode;
        message.ModeLockedForMs = modeLockedForMs;
        message.Members = members;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }

    /// <summary>The recipient is in no party.</summary>
    public static OutboundPacket Empty(PacketEncoder encoder) => Create(0, PartyExperienceMode.Unknown, 0, [], encoder);
}
