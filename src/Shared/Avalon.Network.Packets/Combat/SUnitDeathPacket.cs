using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SUnitDeathPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_UNIT_DEATH;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong UnitGuid { get; set; }
    [ProtoMember(2)] public ulong? KillerGuid { get; set; }

    public static NetworkPacket Create(ObjectGuid unit, ObjectGuid? killer, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SUnitDeathPacket { UnitGuid = unit.RawValue, KillerGuid = killer?.RawValue },
            PacketType, Flags, Protocol, encryptFunc);
}
