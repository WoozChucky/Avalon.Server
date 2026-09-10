using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.State;

[ProtoContract]
public class SInstanceStateAddPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_ADD;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ObjectState> Adds { get; set; }

    public static NetworkPacket Create(List<ObjectState> adds, EncryptFunc encryptFunc)
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

    [ProtoMember(1)] public List<ObjectState> Updates { get; set; }

    public static NetworkPacket Create(List<ObjectState> updates, EncryptFunc encryptFunc)
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

/// <summary>
///     The resource a unit spends on abilities, or None for a unit that spends nothing.
/// </summary>
/// <remarks>
///     Declared beside <see cref="MoveState" /> rather than with the rest of the gameplay
///     enums because it is carried on an entity-state message, and the schema exported for
///     non-.NET clients reaches only the types the packet contracts are built from. Left where
///     it was, a client would have had to copy the four values by hand and would have had no
///     way to notice a renumber.
/// </remarks>
public enum PowerType
{
    None,
    Mana,
    Fury,
    Energy
}
