using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

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

    public static OutboundPacket Create(uint abilityId, CastRejectReason reason, uint cooldownMs,
        PacketEncoder encoder)
    {
        SAbilityNotReadyPacket message = PacketEncoder.Scratch<SAbilityNotReadyPacket>();
        message.AbilityId = abilityId;
        message.CooldownMs = cooldownMs;
        message.Reason = reason;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
