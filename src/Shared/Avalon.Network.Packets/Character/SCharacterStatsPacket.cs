using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// The character sheet (#506): the owner's own attributes, armour, chances, weapon range, movement speed (#627), defences
/// and offence block (#1178), sent only to the character's own connection. Sent when the character enters the world, and
/// again whenever a value changes (a gear change, a level-up, a combat reload that moves a cap), at most once per tick,
/// always whole. The chances and speeds are effective: already clamped to the combat formula's caps, so they are what a
/// hit rolls against and a cast waits by. The speeds and movement speed are fixed at each stats refresh, so a reload moves
/// them at the character's next refresh.
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

    /// <summary>Percentage points, clamped to the formula's CritCap.</summary>
    [ProtoMember(8)] public float CritPct { get; set; }

    /// <summary>Percentage points, clamped to the formula's BlockCap.</summary>
    [ProtoMember(10)] public float BlockPct { get; set; }

    /// <summary>
    /// The range an Attack rolls (#1178): the main hand's first range, or the bare hand's range with no weapon. A weapon's
    /// second range is not shown.
    /// </summary>
    [ProtoMember(11)] public uint WeaponMin { get; set; }

    [ProtoMember(12)] public uint WeaponMax { get; set; }

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

    /// <summary>What a crit multiplies a hit or a heal by (#1178): the formula's CritMultiplier plus the crit multiplier bonus.</summary>
    [ProtoMember(22)] public float CritMultiplier { get; set; }

    /// <summary>
    /// Percentage points (#1178), from 0 to the formula's AttackSpeedCap: a basic attack waits <see cref="AttackTimeMs" />
    /// over 1 + it / 100.
    /// </summary>
    [ProtoMember(23)] public float AttackSpeedPct { get; set; }

    /// <summary>
    /// Percentage points (#1178), from 0 to the formula's CastSpeedCap: every cast time, and a basic spell's use time, is
    /// divided by 1 + it / 100.
    /// </summary>
    [ProtoMember(24)] public float CastSpeedPct { get; set; }

    /// <summary>
    /// Percentage points (#1178), from 0 to the formula's CooldownRecoveryCap: every cooldown but a basic's is divided by
    /// 1 + it / 100.
    /// </summary>
    [ProtoMember(25)] public float CooldownRecoveryPct { get; set; }

    /// <summary>The main hand's time between basic attacks (#1178), the bare hand's with none, before attack speed.</summary>
    [ProtoMember(26)] public uint AttackTimeMs { get; set; }

    /// <summary>
    /// The offence block's increased percentage points (#1178), one per OffenceStat, indexed by its number: Damage 0,
    /// Physical 1, Fire 2, Cold 3, Lightning 4, Blight 5, Attack 6, Spell 7, Area 8, Projectile 9, Melee 10,
    /// DamageOverTime 11, Healing 12, PhysicalAttack 13 (the physical attack damage Strength gives). Each ability's
    /// own amount and pace travel on SCharacterAbilitiesPacket and SCharacterAbilityAmountsPacket, worked out by the
    /// server, so a client never adds these up itself. Not packed, as every repeated scalar here: protobuf-net writes a
    /// packed list of one as a lone element and an empty one as a field of length zero, which a reader generated from the
    /// schema would re-encode differently.
    /// </summary>
    [ProtoMember(27)] public float[] IncreasedPct { get; set; }

    public static OutboundPacket Create(SCharacterStatsPacket sheet, PacketEncoder encoder)
        => encoder.Encode(sheet, PacketType, Flags, Protocol);
}
