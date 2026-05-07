using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Network.Packets.World;
using ProtoBuf;

namespace Avalon.Network.Packets.Abilities;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_CAST_ABILITY)]
public class CCastAbilityPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_CAST_ABILITY;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint AbilityId { get; set; }
    [ProtoMember(2)] public ulong? TargetGuid { get; set; }
    [ProtoMember(3)] public Vector3Dto? GroundPos { get; set; }
}
