using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Combat;

/// <summary>
/// A heal that restored health (#506): sent only when it restored more than 0, to the healer, the target,
/// and every connection within Game:InterestRadius of the target (#532). <see cref="Amount" /> is the health
/// actually restored, overheal left out; <see cref="Result" /> is Crit for a critical heal, since a heal is
/// never dodged or blocked.
/// </summary>
[ProtoContract]
public class SUnitHealedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_UNIT_HEALED;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong Healer { get; set; }
    [ProtoMember(2)] public ulong Target { get; set; }
    [ProtoMember(3)] public uint Amount { get; set; }
    [ProtoMember(4)] public uint CurrentHealth { get; set; }

    /// <summary>The ability that healed; absent when none did.</summary>
    [ProtoMember(5)] public uint? AbilityId { get; set; }

    /// <summary>Crit for a critical heal; absent is None.</summary>
    [ProtoMember(6)] public HitResult Result { get; set; }

    public static NetworkPacket Create(ulong healer, ulong target, uint amount, uint currentHealth, uint? abilityId,
        HitResult result, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SUnitHealedPacket
            {
                Healer = healer, Target = target, Amount = amount, CurrentHealth = currentHealth, AbilityId = abilityId,
                Result = result,
            },
            PacketType, Flags, Protocol, encrypt);
}
