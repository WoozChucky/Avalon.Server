using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// The character sheet (#506): the owner's own attributes, armour, damage stats, chances, weapon range,
/// attack speed and movement speed (#627, #1178), sent only to the character's own connection. Sent when the character
/// enters the world, and again whenever a value changes (a gear change, a level-up, a combat reload that
/// moves a cap), at most once per tick, always whole. The percentages are effective: already clamped to the
/// combat formula's caps, so they are the chances a hit actually rolls against. Attack speed and movement speed
/// are fixed at each stats refresh, so a reload moves them at the character's next refresh.
/// </summary>
[ProtoContract]
[ProtoReserved(9)]
[ProtoReserved("DodgePct")]
public class SCharacterStatsPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_STATS;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint Stamina { get; set; }
    [ProtoMember(2)] public uint Strength { get; set; }
    [ProtoMember(3)] public uint Agility { get; set; }
    [ProtoMember(4)] public uint Intellect { get; set; }
    [ProtoMember(5)] public uint Armor { get; set; }
    [ProtoMember(6)] public uint AttackDamage { get; set; }
    [ProtoMember(7)] public uint AbilityDamage { get; set; }

    /// <summary>Percentage points, clamped to the formula's CritCap.</summary>
    [ProtoMember(8)] public float CritPct { get; set; }

    /// <summary>Percentage points, clamped to the formula's BlockCap.</summary>
    [ProtoMember(10)] public float BlockPct { get; set; }

    /// <summary>The main-hand weapon's damage range; both 0 with no weapon.</summary>
    [ProtoMember(11)] public uint WeaponMin { get; set; }

    [ProtoMember(12)] public uint WeaponMax { get; set; }

    /// <summary>
    /// The effective attack speed in percentage points (#1178), capped by the formula's AttackSpeedCap, until the sheet
    /// carries all three speeds: a basic attack waits its weapon's attack time over 1 + it / 100, the global cooldown not.
    /// </summary>
    [ProtoMember(13)] public float HastePct { get; set; }

    /// <summary>
    /// Metres per second (#627): the speed the server steps this character's input at, the one to predict with.
    /// </summary>
    [ProtoMember(14)] public float MovementSpeed { get; set; }

    /// <summary>The evasion rating (#1178), a flat number; <see cref="EvasionPct" /> is the chance it gives.</summary>
    [ProtoMember(15)] public uint Evasion { get; set; }

    /// <summary>
    /// Percentage points (#1178): the chance to evade a hit from an attacker of the character's own level, clamped to the
    /// formula's EvasionCap. An attacker of another level meets a different chance.
    /// </summary>
    [ProtoMember(16)] public float EvasionPct { get; set; }

    /// <summary>What a full ward holds (#1178). The current ward travels on the character's replicated state.</summary>
    [ProtoMember(17)] public uint MaxWard { get; set; }

    /// <summary>
    /// Percent (#1178): the fire resistance a hit counts, capped at the formula's ResistanceCap and never below its
    /// ResistanceFloor; below 0 is a weakness. The other three resistances are the same.
    /// </summary>
    [ProtoMember(18)] public float FireResistance { get; set; }

    [ProtoMember(19)] public float ColdResistance { get; set; }

    [ProtoMember(20)] public float LightningResistance { get; set; }

    [ProtoMember(21)] public float BlightResistance { get; set; }

    public static OutboundPacket Create(SCharacterStatsPacket sheet, PacketEncoder encoder)
        => encoder.Encode(sheet, PacketType, Flags, Protocol);
}
