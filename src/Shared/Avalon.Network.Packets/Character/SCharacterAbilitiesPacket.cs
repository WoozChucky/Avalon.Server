using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

[ProtoContract]
public class SCharacterAbilitiesPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_ABILITIES;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public AbilityInfo[] Abilities { get; set; }

    public static NetworkPacket Create(AbilityInfo[] abilities, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SCharacterAbilitiesPacket { Abilities = abilities },
            PacketType, Flags, Protocol, encrypt);
}

[ProtoContract]
public class AbilityInfo
{
    [ProtoMember(1)] public uint AbilityId { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
    [ProtoMember(3)] public float Cooldown { get; set; }
    [ProtoMember(4)] public float CastTime { get; set; }
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
    /// The pool <see cref="Cost" /> is spent from (#652); None for a cost of 0. A caster whose pool is another one
    /// cannot cast the ability, and is refused with WrongPowerType.
    /// </summary>
    [ProtoMember(17)] public PowerType CostPowerType { get; set; }

    /// <summary>
    /// What <see cref="AmountMin" /> and <see cref="AmountMax" /> are (#669): Damage or Healing per unit hit, or
    /// None when the ability has no direct amount to state (the two are then 0 and mean nothing). Kept current
    /// after select by SCharacterAbilityAmountsPacket.
    /// </summary>
    [ProtoMember(18)] public AbilityAmountKind AmountKind { get; set; }

    /// <summary>
    /// The normal amount per unit hit, at the low and high end of the caster's weapon roll (#669): its base
    /// (effect value, scaled stat and weapon roll, as combat computes it) floored, before crit, dodge, block and
    /// the target's armour. Damage has a minimum of 1, healing none. With no weapon term the two are equal.
    /// </summary>
    [ProtoMember(19)] public uint AmountMin { get; set; }

    [ProtoMember(20)] public uint AmountMax { get; set; }
}
