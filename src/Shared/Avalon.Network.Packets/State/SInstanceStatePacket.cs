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
    {
        SInstanceStateAddPacket message = PacketEncoder.Scratch<SInstanceStateAddPacket>();
        message.Adds = adds;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}

[ProtoContract]
public class SInstanceStateUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ObjectState> Updates { get; set; }

    public static OutboundPacket Create(List<ObjectState> updates, PacketEncoder encoder)
    {
        SInstanceStateUpdatePacket message = PacketEncoder.Scratch<SInstanceStateUpdatePacket>();
        message.Updates = updates;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}

[ProtoContract]
public class SInstanceStateRemovePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_REMOVE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ulong> Removes { get; set; }

    // One id list per thread, refilled for every remove: the message is encoded before Create returns (#875).
#pragma warning disable IDE1006
    [ThreadStatic] private static List<ulong>? t_ids;
#pragma warning restore IDE1006

    public static OutboundPacket Create(IReadOnlyList<ObjectGuid> removes, PacketEncoder encoder)
    {
        List<ulong> ids = t_ids ??= new List<ulong>(removes.Count);
        ids.Clear();
        // Indexed rather than built by LINQ, which added an iterator to every remove (#640).
        for (int i = 0; i < removes.Count; i++)
            ids.Add(removes[i].RawValue);

        SInstanceStateRemovePacket message = PacketEncoder.Scratch<SInstanceStateRemovePacket>();
        message.Removes = ids;
        return encoder.Encode(message, PacketType, Flags, Protocol);
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
