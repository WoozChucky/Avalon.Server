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

    public static OutboundPacket Create(ulong attacker, ulong target, uint currentHealth, uint damage, uint? abilityId,
        PacketEncoder encoder, HitResult result = HitResult.None, uint? auraId = null)
        => encoder.Encode(
            new SCharacterDamagePacket
            {
                Attacker = attacker,
                Target = target,
                CurrentHealth = currentHealth,
                Damage = damage,
                AbilityId = abilityId,
                Result = result,
                AuraId = auraId,
            },
            PacketType, Flags, Protocol);
}
