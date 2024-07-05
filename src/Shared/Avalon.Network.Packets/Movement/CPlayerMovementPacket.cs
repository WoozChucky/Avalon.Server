using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
public class CPlayerMovementPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_MOVEMENT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public float ElapsedGameTime { get; set; }
    [ProtoMember(2)] public float X { get; set; }
    [ProtoMember(3)] public float Y { get; set; }
    [ProtoMember(4)] public float VelocityX { get; set; }
    [ProtoMember(5)] public float VelocityY { get; set; }

    public static NetworkPacket Create(float time, float x, float y, float velX, float velY, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var p = new CPlayerMovementPacket()
        {
            ElapsedGameTime = time,
            X = x,
            Y = y,
            VelocityX = velX,
            VelocityY = velY
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
