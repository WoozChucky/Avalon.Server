using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterAbilitiesPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_ABILITIES;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public AbilityInfo[] Abilities { get; set; }

    public static OutboundPacket Create(AbilityInfo[] abilities, PacketEncoder encoder)
    {
        SCharacterAbilitiesPacket message = PacketEncoder.Scratch<SCharacterAbilitiesPacket>();
        message.Abilities = abilities;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}

[ProtoContract]
public class AbilityInfo
{
    [ProtoMember(1)] public uint AbilityId { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
    [ProtoMember(5)] public uint Cost { get; set; }
    [ProtoMember(6)] public ushort Range { get; set; }

    /// <summary>Unused since #164 and never set: there is no facing cone. Kept so the field number is not reused.</summary>
    [ProtoMember(7)] public float FacingAngle { get; set; }

    // Aim and shape (#164), so a client can draw the telegraph. Metres and degrees as on AbilityTemplate.
    [ProtoMember(8)] public AbilityAimMode AimMode { get; set; }
    [ProtoMember(9)] public AbilityShape Shape { get; set; }
    [ProtoMember(10)] public AbilityAnchor Anchor { get; set; }
    [ProtoMember(11)] public float Reach { get; set; }
    [ProtoMember(12)] public float Radius { get; set; }
    [ProtoMember(13)] public float ArcDegrees { get; set; }
    [ProtoMember(14)] public float ProjectileSpeed { get; set; }
    [ProtoMember(15)] public bool Pierce { get; set; }
    [ProtoMember(16)] public AbilityAffects Affects { get; set; }

    /// <summary>
    /// What <see cref="AmountMin" /> and <see cref="AmountMax" /> are (#669): Damage or Healing per unit hit, or
    /// None when the ability has no direct amount to state (the two are then 0 and mean nothing). Kept current
    /// after select by SCharacterAbilityAmountsPacket.
    /// </summary>
    [ProtoMember(18)] public AbilityAmountKind AmountKind { get; set; }

    /// <summary>
    /// The normal amount per unit hit, with every roll at the low and then the high end of its range (#669, #1178): its
    /// base (an Attack's weapon roll, a Spell's or a Heal's spell base, scaled by the caster's offence, as combat computes
    /// it) floored, before crit, evasion, block and the target's defences. Damage has a minimum of 1, healing none.
    /// </summary>
    [ProtoMember(19)] public uint AmountMin { get; set; }

    [ProtoMember(20)] public uint AmountMax { get; set; }

    /// <summary>What the ability is (#1178): the tags its damage and healing scale by.</summary>
    [ProtoMember(21)] public AbilityTags Tags { get; set; }

    /// <summary>
    /// The damage types the amount is dealt as (#1178): every type a hit at the high end of its rolls has a part of. None
    /// for a heal, for no direct amount, or for a hit with nothing to roll.
    /// </summary>
    [ProtoMember(22)] public DamageTypeMask AmountTypes { get; set; }

    /// <summary>
    /// Milliseconds, rounded to the nearest (#1178): the cooldown a cast sets now, worked out by the server from the
    /// caster's speeds (a basic attack's from its weapon's attack time), so a client never applies the rules itself. With
    /// <see cref="CastTimeMs" />, the only pace sent: the row's own cooldown is the ability catalog's cooldownMs. Kept
    /// current after select by SCharacterAbilityAmountsPacket.
    /// </summary>
    [ProtoMember(23)] public uint CooldownMs { get; set; }

    /// <summary>Milliseconds, rounded to the nearest (#1178): the cast time over the caster's cast speed.</summary>
    [ProtoMember(24)] public uint CastTimeMs { get; set; }

    // The shape the capstones a character holds give the ability (#1181 Part D); Reach, Radius and ArcDegrees above are
    // already shaped. An unshaped ability sends 1 projectile and 0 for the rest.

    /// <summary>Projectile only: how many fly, in a fan 10 degrees apart, centred on the aim; 1 otherwise.</summary>
    [ProtoMember(25)] public uint ProjectileCount { get; set; }

    /// <summary>Metres: the circle a projectile's explosion hits where it ends on a unit; 0 for none.</summary>
    [ProtoMember(26)] public float ExplosionRadius { get; set; }

    /// <summary>Percent of the amount the explosion hits for.</summary>
    [ProtoMember(27)] public float ExplosionEffectiveness { get; set; }

    /// <summary>Percent of the amount each affected unit is hit, or healed, again at once; 0 for no second hit.</summary>
    [ProtoMember(28)] public float SecondHitEffectiveness { get; set; }

    /// <summary>How many stacks above its own cap the caster's aura may reach.</summary>
    [ProtoMember(29)] public uint AuraStacksBonus { get; set; }

    /// <summary>Percent the caster's aura lasts longer.</summary>
    [ProtoMember(30)] public float AuraDurationPct { get; set; }
}
