using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;
using Avalon.Network.Packets.Serialization;

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

    public static NetworkPacket Create(ObjectGuid caster, float castTime, uint abilityId, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SUnitStartCastPacket { Caster = caster.RawValue, CastTime = castTime, AbilityId = abilityId },
            PacketType, Flags, Protocol, encryptFunc);
}
