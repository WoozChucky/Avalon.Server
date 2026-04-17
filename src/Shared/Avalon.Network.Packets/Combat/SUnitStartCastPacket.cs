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

    public static NetworkPacket Create(ObjectGuid caster, float castTime, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SUnitStartCastPacket { Caster = caster.RawValue, CastTime = castTime },
            PacketType, Flags, Protocol, encryptFunc);
}
