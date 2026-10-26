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

    /// <summary>
    /// The world tick whose state the packet carries (#1066): the number of the world loop's tick that built it, the
    /// first being 0. The time-sync ping anchors the same count to server time (<c>SPingPacket.WorldTick</c>). Written
    /// after <see cref="Adds" />, as the packet's last member.
    /// </summary>
    [ProtoMember(2)] public long WorldTick { get; set; }

    // One single-entry list per thread, for WriteEntry: the entry is written before WriteEntry returns.
#pragma warning disable IDE1006
    [ThreadStatic] private static List<ObjectState>? t_entry;
#pragma warning restore IDE1006

    public static OutboundPacket Create(List<ObjectState> adds, long worldTick, PacketEncoder encoder)
    {
        SInstanceStateAddPacket message = PacketEncoder.Scratch<SInstanceStateAddPacket>();
        message.Adds = adds;
        message.WorldTick = worldTick;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }

    /// <summary>
    /// Writes <paramref name="worldTick" /> at <paramref name="destination" />'s position as <see cref="WorldTick" />,
    /// byte for byte as <see cref="Create" /> writes it after the entries (nothing for tick 0): see
    /// <see cref="SInstanceStateUpdatePacket.WriteTick" />.
    /// </summary>
    public static void WriteTick(long worldTick, MemoryStream destination)
    {
        SInstanceStateAddPacket message = PacketEncoder.Scratch<SInstanceStateAddPacket>();
        message.WorldTick = worldTick;
        PacketEncoder.Write(message, destination);
    }

    /// <summary>
    /// Writes <paramref name="add" /> at <paramref name="destination" />'s position as one entry of <see cref="Adds" />,
    /// byte for byte as <see cref="Create" /> writes it inside a packet (#980): see
    /// <see cref="SInstanceStateUpdatePacket.WriteEntry" />, whose layout this packet shares.
    /// </summary>
    public static void WriteEntry(ObjectState add, MemoryStream destination)
    {
        List<ObjectState> entry = t_entry ??= new List<ObjectState>(1);
        entry.Add(add);
        try
        {
            SInstanceStateAddPacket message = PacketEncoder.Scratch<SInstanceStateAddPacket>();
            message.Adds = entry;
            PacketEncoder.Write(message, destination);
        }
        finally
        {
            entry.Clear();
        }
    }
}

[ProtoContract]
public class SInstanceStateUpdatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_WORLD_STATE_UPDATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<ObjectState> Updates { get; set; }

    /// <summary>
    /// The world tick whose state the packet carries (#1066): the number of the world loop's tick that built it, the
    /// first being 0. The time-sync ping anchors the same count to server time (<c>SPingPacket.WorldTick</c>). Written
    /// after <see cref="Updates" />, as the packet's last member.
    /// </summary>
    [ProtoMember(2)] public long WorldTick { get; set; }

    // One single-entry list per thread, for WriteEntry: the entry is written before WriteEntry returns.
#pragma warning disable IDE1006
    [ThreadStatic] private static List<ObjectState>? t_entry;
#pragma warning restore IDE1006

    public static OutboundPacket Create(List<ObjectState> updates, long worldTick, PacketEncoder encoder)
    {
        SInstanceStateUpdatePacket message = PacketEncoder.Scratch<SInstanceStateUpdatePacket>();
        message.Updates = updates;
        message.WorldTick = worldTick;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }

    /// <summary>
    /// Writes <paramref name="update" /> at <paramref name="destination" />'s position as one entry of
    /// <see cref="Updates" />, byte for byte as <see cref="Create" /> writes it inside a packet (#980).
    /// </summary>
    /// <remarks>
    /// The message has two members, a repeated message and the tick, and protobuf-net writes them in field order: each
    /// entry on its own (its tag, its length, its bytes), then the tick (<see cref="WriteTick" />). So a packet's payload
    /// is its entries one after another, in order, then its tick. A payload put together from entries written here, each
    /// written once and copied into every packet that carries it, followed by the broadcast's tick, is the payload
    /// <see cref="Create" /> writes for the same list and tick (<see cref="PacketEncoder.FromPayload" />). Written through
    /// a packet of one entry, so the tag and the framing are the contract's own.
    /// </remarks>
    public static void WriteEntry(ObjectState update, MemoryStream destination)
    {
        List<ObjectState> entry = t_entry ??= new List<ObjectState>(1);
        entry.Add(update);
        try
        {
            SInstanceStateUpdatePacket message = PacketEncoder.Scratch<SInstanceStateUpdatePacket>();
            message.Updates = entry;
            PacketEncoder.Write(message, destination);
        }
        finally
        {
            entry.Clear();
        }
    }

    /// <summary>
    /// Writes <paramref name="worldTick" /> at <paramref name="destination" />'s position as <see cref="WorldTick" />,
    /// byte for byte as <see cref="Create" /> writes it after the entries (#1066): nothing for tick 0, which protobuf-net
    /// leaves out as a default. Written through a packet with no entry, so the tag and the varint are the contract's own.
    /// </summary>
    public static void WriteTick(long worldTick, MemoryStream destination)
    {
        SInstanceStateUpdatePacket message = PacketEncoder.Scratch<SInstanceStateUpdatePacket>();
        message.WorldTick = worldTick;
        PacketEncoder.Write(message, destination);
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

    /// <summary>Never sent since #1181: every character spends mana.</summary>
    Fury,

    /// <summary>Never sent since #1181: every character spends mana.</summary>
    Energy
}
