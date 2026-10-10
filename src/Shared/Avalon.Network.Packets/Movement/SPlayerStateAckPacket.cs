using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
public class SPlayerStateAckPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_PLAYER_STATE_ACK;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint Seq { get; set; }
    [ProtoMember(2)] public float X { get; set; }
    [ProtoMember(3)] public float Y { get; set; }
    [ProtoMember(4)] public float Z { get; set; }
    [ProtoMember(5)] public float VelX { get; set; }
    [ProtoMember(6)] public float VelZ { get; set; }
    [ProtoMember(7)] public ushort YawDeg { get; set; }

    public static OutboundPacket Create(uint seq, float x, float y, float z, float velX, float velZ, ushort yawDeg, PacketEncoder encoder)
    {
        SPlayerStateAckPacket message = PacketEncoder.Scratch<SPlayerStateAckPacket>();
        message.Seq = seq;
        message.X = x;
        message.Y = y;
        message.Z = z;
        message.VelX = velX;
        message.VelZ = velZ;
        message.YawDeg = yawDeg;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
