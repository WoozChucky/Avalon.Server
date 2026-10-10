using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;
using NetworkPacketFlags = Avalon.Network.Packets.Abstractions.NetworkPacketFlags;
using NetworkProtocol = Avalon.Network.Packets.Abstractions.NetworkProtocol;

namespace Avalon.Network.Packets.State;

[ProtoContract]
public class SInstanceStateAddPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_ADD;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ObjectState> Adds { get; set; }

    public static OutboundPacket Create(List<ObjectState> adds, PacketEncoder encoder)
        => encoder.Encode(
            new SInstanceStateAddPacket { Adds = adds },
            PacketType, Flags, Protocol);
}

[ProtoContract]
public class SInstanceStateUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ObjectState> Updates { get; set; }

    public static OutboundPacket Create(List<ObjectState> updates, PacketEncoder encoder)
        => encoder.Encode(
            new SInstanceStateUpdatePacket { Updates = updates },
            PacketType, Flags, Protocol);
}

[ProtoContract]
public class SInstanceStateRemovePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_REMOVE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ulong> Removes { get; set; }

    public static OutboundPacket Create(IReadOnlyList<ObjectGuid> removes, PacketEncoder encoder)
    {
        // Sized and indexed rather than built by LINQ, which added an iterator to every remove (#640).
        var ids = new List<ulong>(removes.Count);
        for (int i = 0; i < removes.Count; i++)
            ids.Add(removes[i].RawValue);

        return encoder.Encode(
            new SInstanceStateRemovePacket { Removes = ids },
            PacketType, Flags, Protocol);
    }
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
