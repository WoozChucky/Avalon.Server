using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.World;

[ProtoContract]
public class SWorldGameStatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public GameStateUpdateType UpdateType { get; set; }
    [ProtoMember(2)] public EntityType EntityType { get; set; }
    [ProtoMember(3)] public List<CreatureStateNew> New { get; set; } = [];
    [ProtoMember(4)] public List<CreatureStateUpdate> Updates { get; set; } = [];
    
    public static NetworkPacket Create(List<CreatureStateNew> objects, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var packet = new SWorldGameStatePacket
        {
            UpdateType = GameStateUpdateType.Create,
            EntityType = EntityType.Creature,
            New = objects
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
    
    public static NetworkPacket Create(List<CreatureStateUpdate> objects, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var packet = new SWorldGameStatePacket
        {
            UpdateType = GameStateUpdateType.Update,
            EntityType = EntityType.Creature,
            Updates = objects
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

public enum GameStateUpdateType
{
    Create,
    Update,
    Destroy
}

public enum EntityType
{
    Creature,
    Character,
    GameObject
}

[ProtoContract]
public class CreatureStateNew
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public float Rotation { get; set; }
    public uint Health { get; set; }
    public uint Level { get; set; }
    // TODO: Add more properties
}

[ProtoContract]
public class CreatureStateUpdate
{
    public Guid Id { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public float Rotation { get; set; }
    public uint Health { get; set; }
}
