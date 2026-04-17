using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.State;

[ProtoContract]
public class ObjectAdd
{
    [ProtoMember(1)] public ulong Guid { get; set; }
    [ProtoMember(2)] public ReadOnlyMemory<byte> Fields { get; set; }
}

[ProtoContract]
public class ObjectUpdate
{
    [ProtoMember(1)] public ulong Guid { get; set; }
    [ProtoMember(2)] public ReadOnlyMemory<byte> Fields { get; set; }
}

[ProtoContract]
public class SInstanceStateAddPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_ADD;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ObjectAdd> Adds { get; set; }

    public static NetworkPacket Create(List<ObjectAdd> adds, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SInstanceStateAddPacket { Adds = adds },
            PacketType, Flags, Protocol, encryptFunc);
}

[ProtoContract]
public class SInstanceStateUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ObjectUpdate> Updates { get; set; }

    public static NetworkPacket Create(List<ObjectUpdate> updates, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SInstanceStateUpdatePacket { Updates = updates },
            PacketType, Flags, Protocol, encryptFunc);
}

[ProtoContract]
public class SInstanceStateRemovePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_REMOVE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ulong> Removes { get; set; }

    public static NetworkPacket Create(IEnumerable<ObjectGuid> removes, EncryptFunc encryptFunc)
        => PacketSerializationHelper.Serialize(
            new SInstanceStateRemovePacket { Removes = removes.Select(r => r.RawValue).ToList() },
            PacketType, Flags, Protocol, encryptFunc);
}

public enum MoveState
{
    Idle,
    Walking,
    Running,
    Swimming
}
