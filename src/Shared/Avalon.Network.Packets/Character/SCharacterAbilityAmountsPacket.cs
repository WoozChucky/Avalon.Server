using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// The current per-hit amount and pace of every ability the character knows (#669, #1178), to its own connection only.
/// The same values SCharacterAbilitiesPacket's AbilityInfo carries at select, sent again, whole, whenever one of
/// them changes: after a stats refresh (a gear change, a level-up) moves the offence, weapon or speed an ability
/// scales or is paced by. Sent on the tick the change is made, in the same flush as the SCharacterStatsPacket that
/// shows the new stats. A client replaces every ability's amount with the entries here; an ability absent
/// from the list keeps no amount.
/// </summary>
[ProtoContract]
public class SCharacterAbilityAmountsPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_ABILITY_AMOUNTS;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public AbilityAmountInfo[] Amounts { get; set; } = [];

    public static OutboundPacket Create(AbilityAmountInfo[] amounts, PacketEncoder encoder)
    {
        SCharacterAbilityAmountsPacket message = PacketEncoder.Scratch<SCharacterAbilityAmountsPacket>();
        message.Amounts = amounts;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}

/// <summary>
/// One ability's amount and pace (#669, #1178), with the meaning of AbilityInfo's AmountKind, AmountMin, AmountMax,
/// AmountTypes, CooldownMs and CastTimeMs.
/// </summary>
[ProtoContract]
public class AbilityAmountInfo
{
    [ProtoMember(1)] public uint AbilityId { get; set; }
    [ProtoMember(2)] public AbilityAmountKind Kind { get; set; }
    [ProtoMember(3)] public uint Min { get; set; }
    [ProtoMember(4)] public uint Max { get; set; }
    [ProtoMember(5)] public DamageTypeMask Types { get; set; }
    [ProtoMember(6)] public uint CooldownMs { get; set; }
    [ProtoMember(7)] public uint CastTimeMs { get; set; }
}
