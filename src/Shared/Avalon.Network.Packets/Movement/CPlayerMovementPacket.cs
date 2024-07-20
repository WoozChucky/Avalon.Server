using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using ProtoBuf;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
[Packet(HandleOn = ComponentType.World, Type = NetworkPacketType.CMSG_MOVEMENT)]
public class CPlayerMovementPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_MOVEMENT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public double Timestamp { get; set; }
    [ProtoMember(2)] public float X { get; set; }
    [ProtoMember(3)] public float Y { get; set; }
    [ProtoMember(4)] public float Z { get; set; }
    [ProtoMember(5)] public float VelocityX { get; set; }
    [ProtoMember(6)] public float VelocityY { get; set; }
    [ProtoMember(7)] public float VelocityZ { get; set; }
    [ProtoMember(8)] public float Rotation { get; set; }
    // [ProtoMember(9)] public float Strafe { get; set; } // -1.0f to 1.0f (left to right)

    public static NetworkPacket Create(double time, float x, float y, float z, float velX, float velY, float velZ, float rotation, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var p = new CPlayerMovementPacket
        {
            Timestamp = time,
            X = x,
            Y = y,
            Z = z,
            VelocityX = velX,
            VelocityY = velY,
            VelocityZ = velZ,
            Rotation = rotation
        };
        
        Serializer.Serialize(memoryStream, p);
        
        var buffer = encryptFunc(memoryStream.ToArray());
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer
        };
    }
}
