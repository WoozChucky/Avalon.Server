using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SUnitFinishCastPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_UNIT_FINISH_CAST;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong Caster { get; set; }
    [ProtoMember(2)] public uint AbilityId { get; set; }

    /// <summary>
    /// The cast this belongs to (#648): the id its <c>SUnitStartCastPacket</c> carried, unique within the instance,
    /// so a client clears the telegraph of that cast and no other. 0 from a server before #648.
    /// </summary>
    [ProtoMember(3)] public uint CastId { get; set; }

    public static NetworkPacket Create(ObjectGuid caster, AbilityId ability, uint castId, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SUnitFinishCastPacket { Caster = caster.RawValue, AbilityId = ability.Value, CastId = castId },
            PacketType, Flags, Protocol, encryptFunc);
}
