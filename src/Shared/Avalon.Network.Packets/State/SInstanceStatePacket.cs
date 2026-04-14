using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.State;

[ProtoContract]
public class ObjectAdd
{
    [ProtoMember(1)] public ulong Guid { get; set; }
    [ProtoMember(2)] public byte[] Fields { get; set; } = [];
}

[ProtoContract]
public class ObjectUpdate
{
    [ProtoMember(1)] public ulong Guid { get; set; }
    [ProtoMember(2)] public byte[] Fields { get; set; } = [];
}

[ProtoContract]
public class SInstanceStateAddPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_ADD;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ObjectAdd> Adds { get; set; }

    public static NetworkPacket Create(List<ObjectAdd> adds, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new SInstanceStateAddPacket()
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
public class SInstanceStateUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ObjectUpdate> Updates { get; set; }

    public static NetworkPacket Create(List<ObjectUpdate> updates, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new SInstanceStateUpdatePacket()
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
public class SInstanceStateRemovePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_REMOVE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ulong> Removes { get; set; }

    public static NetworkPacket Create(IEnumerable<ObjectGuid> removes, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var p = new SInstanceStateRemovePacket()
        {
            Removes = removes.Select(r => r.RawValue).ToList()
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

public enum MoveState
{
    Idle,
    Walking,
    Running,
    Swimming
}
