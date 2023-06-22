using ProtoBuf;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
public class CPlayerMovementPacket
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_MOVEMENT;
    
    [ProtoMember(1)] public float ElapsedGameTime { get; set; }
    [ProtoMember(2)] public float X { get; set; }
    [ProtoMember(3)] public float Y { get; set; }
    
    public static NetworkPacket Create(float time, float x, float y)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new CPlayerMovementPacket()
        {
            ElapsedGameTime = time,
            X = x,
            Y = y,
        };
        
        Serializer.Serialize(memoryStream, movementPacket);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.None,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
