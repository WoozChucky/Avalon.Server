using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Movement;

[ProtoContract]
public class SNpcUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_NPC_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public CreaturePacket[] Creatures { get; set; }
    [ProtoMember(2)] public long ServerTicks { get; set; }
    
    public static NetworkPacket Create(CreaturePacket[] creatures, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var packet = new SNpcUpdatePacket()
        {
            Creatures = creatures,
            ServerTicks = DateTime.UtcNow.Ticks
        };
        
        Serializer.Serialize(memoryStream, packet);
        
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

public enum MoveState
{
    Idle,
    Walking,
    Running,
}

[ProtoContract]
public class CreaturePacket
{
    [ProtoMember(1)] public Guid Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
    [ProtoMember(3)] public float PositionX { get; set; }
    [ProtoMember(4)] public float PositionY { get; set; }
    [ProtoMember(5)] public float PositionZ { get; set; }
    [ProtoMember(6)] public float VelocityX { get; set; }
    [ProtoMember(7)] public float VelocityY { get; set; }
    [ProtoMember(8)] public float VelocityZ { get; set; }
    [ProtoMember(9)] public float Orientation { get; set; }
    [ProtoMember(10)] public MoveState MoveState { get; set; }
}
