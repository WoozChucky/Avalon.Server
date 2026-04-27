using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

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

    public static NetworkPacket Create(uint seq, float x, float y, float z, float velX, float velZ, ushort yawDeg, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SPlayerStateAckPacket
            {
                Seq = seq,
                X = x, Y = y, Z = z,
                VelX = velX, VelZ = velZ,
                YawDeg = yawDeg,
            },
            PacketType, Flags, Protocol, encryptFunc);
}
