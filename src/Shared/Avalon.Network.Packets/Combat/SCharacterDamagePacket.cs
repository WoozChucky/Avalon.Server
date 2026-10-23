using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SCharacterDamagePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_DAMAGED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong Attacker { get; set; }
    [ProtoMember(2)] public ulong Target { get; set; }
    [ProtoMember(3)] public uint CurrentHealth { get; set; }
    [ProtoMember(4)] public uint Damage { get; set; }
    [ProtoMember(5)] public uint? AbilityId { get; set; }

    /// <summary>How the hit went (#506): a crit, a block, both, or a dodge, which deals 0. Absent is None.</summary>
    [ProtoMember(6)] public HitResult Result { get; set; }

    /// <summary>The aura whose tick this was (auras); absent for every other hit.</summary>
    [ProtoMember(7)] public uint? AuraId { get; set; }

    /// <summary>
    /// The hit's damage type (#1178): the part that dealt the most after mitigation, a tie going to the lower type. A dodge
    /// names the type it would have dealt.
    /// </summary>
    [ProtoMember(8)] public DamageType Type { get; set; }

    /// <summary>Every damage type the hit carried (#1178), its parts above 0 before mitigation; None for a hit of nothing.</summary>
    [ProtoMember(9)] public DamageTypeMask Types { get; set; }

    /// <summary>What the character's ward took of <see cref="Damage" /> (#1178), which is the whole hit, ward included.</summary>
    [ProtoMember(10)] public uint WardAbsorbed { get; set; }

    public static OutboundPacket Create(ulong attacker, ulong target, uint currentHealth, uint damage, uint? abilityId,
        PacketEncoder encoder, HitResult result = HitResult.None, uint? auraId = null, DamageType type = DamageType.Physical,
        DamageTypeMask types = DamageTypeMask.None, uint wardAbsorbed = 0)
    {
        SCharacterDamagePacket message = PacketEncoder.Scratch<SCharacterDamagePacket>();
        message.Attacker = attacker;
        message.Target = target;
        message.CurrentHealth = currentHealth;
        message.Damage = damage;
        message.AbilityId = abilityId;
        message.Result = result;
        message.AuraId = auraId;
        message.Type = type;
        message.Types = types;
        message.WardAbsorbed = wardAbsorbed;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
