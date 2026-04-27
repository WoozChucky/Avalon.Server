using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_PLAYER_INPUT)]
public class CPlayerInputPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_PLAYER_INPUT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint Seq { get; set; }
    [ProtoMember(2)] public float DirX { get; set; }
    [ProtoMember(3)] public float DirZ { get; set; }
    [ProtoMember(4)] public ushort YawDeg { get; set; }
    [ProtoMember(5)] public byte InputFlags { get; set; }
}
