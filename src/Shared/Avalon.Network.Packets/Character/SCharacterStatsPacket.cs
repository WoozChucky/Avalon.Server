using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// The character sheet (#506): the owner's own attributes, armour, damage stats, chances, weapon range,
/// haste and movement speed (#627), sent only to the character's own connection. Sent when the character
/// enters the world, and again whenever a value changes (a gear change, a level-up, a combat reload that
/// moves a cap), at most once per tick, always whole. The percentages are effective: already clamped to the
/// combat formula's caps, so they are the chances a hit actually rolls against. Haste and movement speed
/// are fixed at each stats refresh, so a reload moves them at the character's next refresh.
/// </summary>
[ProtoContract]
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

    /// <summary>Percentage points, clamped to the formula's DodgeCap.</summary>
    [ProtoMember(9)] public float DodgePct { get; set; }

    /// <summary>Percentage points, clamped to the formula's BlockCap.</summary>
    [ProtoMember(10)] public float BlockPct { get; set; }

    /// <summary>The main-hand weapon's damage range; both 0 with no weapon.</summary>
    [ProtoMember(11)] public uint WeaponMin { get; set; }

    [ProtoMember(12)] public uint WeaponMax { get; set; }

    /// <summary>
    /// Haste in percentage points (#627): the gear's AttackSpeed total, capped by the formula's HasteCap. Every
    /// ability's cooldown and cast time is divided by 1 + HastePct / 100; the global cooldown is not.
    /// </summary>
    [ProtoMember(13)] public float HastePct { get; set; }

    /// <summary>
    /// Metres per second (#627): the speed the server steps this character's input at, the one to predict with.
    /// </summary>
    [ProtoMember(14)] public float MovementSpeed { get; set; }

    public static NetworkPacket Create(SCharacterStatsPacket sheet, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(sheet, PacketType, Flags, Protocol, encrypt);
}
