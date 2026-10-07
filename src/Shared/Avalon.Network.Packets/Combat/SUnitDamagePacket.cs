using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SUnitDamagePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CREATURE_DAMAGED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong Attacker { get; set; }
    [ProtoMember(2)] public ulong Target { get; set; }
    [ProtoMember(3)] public uint CurrentHealth { get; set; }
    [ProtoMember(4)] public uint Damage { get; set; }

    /// <summary>How the hit went (#506): a crit, a block, both, or a dodge, which deals 0. Absent is None.</summary>
    [ProtoMember(5)] public HitResult Result { get; set; }

    /// <summary>The aura whose tick this was (auras); absent for every other hit.</summary>
    [ProtoMember(6)] public uint? AuraId { get; set; }

    public static NetworkPacket Create(ObjectGuid attacker, ulong target, uint currentHealth, uint damage, EncryptFunc encryptFunc,
        HitResult result = HitResult.None, uint? auraId = null)
        => PacketSerializationHelper.Serialize(
            new SUnitDamagePacket
            {
                Attacker = attacker.RawValue,
                Target = target,
                CurrentHealth = currentHealth,
                Damage = damage,
                Result = result,
                AuraId = auraId,
            },
            PacketType, Flags, Protocol, encryptFunc);
}
