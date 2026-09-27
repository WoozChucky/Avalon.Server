using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

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

    public static NetworkPacket Create(ulong attacker, ulong target, uint currentHealth, uint damage, uint? abilityId,
        EncryptFunc encryptFunc, HitResult result = HitResult.None)
        => PacketSerializationHelper.Serialize(
            new SCharacterDamagePacket
            {
                Attacker = attacker, Target = target, CurrentHealth = currentHealth, Damage = damage, AbilityId = abilityId,
                Result = result,
            },
            PacketType, Flags, Protocol, encryptFunc);
}
