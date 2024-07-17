using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.State;

[ProtoContract]
public class SCreatureAddedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CREATURE_ADD;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public List<CreatureAdd> Adds { get; set; }

    public static NetworkPacket Create(List<CreatureAdd> adds, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new SCreatureAddedPacket()
        {
            Adds = adds
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

[ProtoContract]
public class SCreatureUpdatedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CREATURE_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public List<CreatureUpdate> Updates { get; set; }

    public static NetworkPacket Create(List<CreatureUpdate> updates, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new SCreatureUpdatedPacket()
        {
            Updates = updates
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

[ProtoContract]
public class SCreatureRemovedPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CREATURE_REMOVE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;
    
    [ProtoMember(1)] public List<ulong> Removes { get; set; }

    public static NetworkPacket Create(List<ulong> removes, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new SCreatureRemovedPacket()
        {
            Removes = removes
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

[ProtoContract]
public class CreatureAdd
{
    [ProtoMember(1)] public ulong Id { get; set; }
    [ProtoMember(2)] public ulong TemplateId { get; set; }
    [ProtoMember(3)] public string Name { get; set; }
    [ProtoMember(4)] public uint Health { get; set; }
    [ProtoMember(5)] public uint? Power { get; set; }
    [ProtoMember(6)] public ushort Level { get; set; }
    [ProtoMember(7)] public float PositionX { get; set; }
    [ProtoMember(8)] public float PositionY { get; set; }
    [ProtoMember(9)] public float PositionZ { get; set; }
    [ProtoMember(10)] public float VelocityX { get; set; }
    [ProtoMember(11)] public float VelocityY { get; set; }
    [ProtoMember(12)] public float VelocityZ { get; set; }
    [ProtoMember(13)] public float Orientation { get; set; }
    [ProtoMember(14)] public MoveState MoveState { get; set; }
}
    
[ProtoContract]
public class CreatureUpdate
{
    [ProtoMember(1)] public ulong Id { get; set; }
    [ProtoMember(2)] public uint CurrentHealth { get; set; }
    [ProtoMember(3)] public uint? CurrentPower { get; set; }
    [ProtoMember(4)] public float PositionX { get; set; }
    [ProtoMember(5)] public float PositionY { get; set; }
    [ProtoMember(6)] public float PositionZ { get; set; }
    [ProtoMember(7)] public float VelocityX { get; set; }
    [ProtoMember(8)] public float VelocityY { get; set; }
    [ProtoMember(9)] public float VelocityZ { get; set; }
    [ProtoMember(10)] public float Orientation { get; set; }
    [ProtoMember(11)] public MoveState MoveState { get; set; }
    [ProtoMember(12)] public bool Alive { get; set; }
}

public enum MoveState
{
    Idle,
    Walking,
    Running,
}
