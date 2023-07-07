using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
public class SNpcUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_NPC_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Udp;

    [ProtoMember(1)] public Guid Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
    [ProtoMember(3)] public float PositionX { get; set; }
    [ProtoMember(4)] public float PositionY { get; set; }
    [ProtoMember(5)] public float VelocityX { get; set; }
    [ProtoMember(6)] public float VelocityY { get; set; }
    
    public static NetworkPacket Create(Guid id, string name, float x, float y, float velX, float velY)
    {
        using var memoryStream = new MemoryStream();
        
        var movementPacket = new SNpcUpdatePacket()
        {
            Id = id,
            Name = name,
            PositionX = x,
            PositionY = y,
            VelocityX = velX,
            VelocityY = velY
        };
        
        Serializer.Serialize(memoryStream, movementPacket);
        
        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = NetworkPacketFlags.None,
                Protocol = Protocol,
                Version = 0
            },
            Payload = memoryStream.ToArray()
        };
    }
}
