using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.World;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.Combat;

[ProtoContract]
public class SUnitRevivePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_UNIT_REVIVE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong UnitGuid { get; set; }
    [ProtoMember(2)] public Vector3Dto Position { get; set; } = new();
    [ProtoMember(3)] public uint Health { get; set; }

    public static NetworkPacket Create(ObjectGuid unit, Vector3 position, uint health, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SUnitRevivePacket
            {
                UnitGuid = unit.RawValue,
                Position = Vector3Dto.From(position),
                Health = health
            },
            PacketType, Flags, Protocol, encryptFunc);
}
