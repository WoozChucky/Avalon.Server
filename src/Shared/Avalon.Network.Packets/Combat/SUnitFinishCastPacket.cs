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

    public static NetworkPacket Create(ObjectGuid caster, AbilityId ability, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SUnitFinishCastPacket { Caster = caster.RawValue, AbilityId = ability.Value },
            PacketType, Flags, Protocol, encryptFunc);
}
