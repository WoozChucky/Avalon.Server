using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Combat;

/// <summary>
/// The answer to a refused CCastAbilityPacket, sent to the caster only, once per refusal (#512).
/// The name predates the reasons: it answers every refusal, not only readiness.
/// </summary>
[ProtoContract]
public class SAbilityNotReadyPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_ABILITY_NOT_READY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint AbilityId { get; set; }

    /// <summary>
    /// Milliseconds left before the cast could succeed, for <see cref="CastRejectReason.Gcd" /> and
    /// <see cref="CastRejectReason.Cooldown" />; 0 for every other reason.
    /// </summary>
    [ProtoMember(2)] public uint CooldownMs { get; set; }

    /// <summary>Why the cast was refused. <see cref="CastRejectReason.Unknown" /> when absent.</summary>
    [ProtoMember(3)] public CastRejectReason Reason { get; set; }

    public static NetworkPacket Create(uint abilityId, CastRejectReason reason, uint cooldownMs,
        EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SAbilityNotReadyPacket { AbilityId = abilityId, CooldownMs = cooldownMs, Reason = reason },
            PacketType, Flags, Protocol, encryptFunc);
}
