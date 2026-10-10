using Avalon.Common;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SUnitStartCastPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_UNIT_START_CAST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong Caster { get; set; }
    [ProtoMember(2)] public float CastTime { get; set; }

    /// <summary>The ability being cast, so other clients can tell which one (#521 item 9). Additive: older payloads decode it as 0.</summary>
    [ProtoMember(3)] public uint AbilityId { get; set; }

    /// <summary>
    /// This cast's id (#648), unique within the instance and never 0. The finish, the interrupt and the fired
    /// broadcast of the same cast carry it, so a client keys a telegraph by caster and cast id. 0 from a server
    /// before #648.
    /// </summary>
    [ProtoMember(4)] public uint CastId { get; set; }

    /// <summary>
    /// Where the cast will land (#648), fixed for the whole cast: draw it for <see cref="CastTime" /> seconds, or
    /// until the finish or the interrupt with this <see cref="CastId" /> arrives. Absent when the server could
    /// not resolve one.
    /// </summary>
    [ProtoMember(5)] public AbilityFootprintDto? Footprint { get; set; }

    /// <summary>
    /// The item whose cast bar this is (item use), with <see cref="AbilityId" /> 0; 0 on an ability's cast.
    /// The finish and the interrupt with this <see cref="CastId" /> carry it too.
    /// </summary>
    [ProtoMember(6)] public ulong ItemTemplateId { get; set; }

    /// <summary>An item's cast bar: no ability, no footprint.</summary>
    public static OutboundPacket CreateForItem(ObjectGuid caster, float castTime, ulong itemTemplateId, uint castId,
        PacketEncoder encoder)
    {
        SUnitStartCastPacket message = PacketEncoder.Scratch<SUnitStartCastPacket>();
        message.Caster = caster.RawValue;
        message.CastTime = castTime;
        message.CastId = castId;
        message.ItemTemplateId = itemTemplateId;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }

    public static OutboundPacket Create(ObjectGuid caster, float castTime, uint abilityId, uint castId,
        AbilityFootprintDto? footprint, PacketEncoder encoder)
    {
        SUnitStartCastPacket message = PacketEncoder.Scratch<SUnitStartCastPacket>();
        message.Caster = caster.RawValue;
        message.CastTime = castTime;
        message.AbilityId = abilityId;
        message.CastId = castId;
        message.Footprint = footprint;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
