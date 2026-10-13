using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Serialization;

/// <summary>
/// #883: a server prepares every packet it sends before its port opens, so nothing a send needs is built by the first
/// packet of its type, which a world sends on the tick, and a packet that could never be sent stops the start. The
/// packets here are this class's own: no other test encodes them, so what is built for them is built here.
/// </summary>
public class PacketEncoderPrepareShould
{
    public enum PreparedKind
    {
        None = 0,
        Some = 3,
    }

    [ProtoContract]
    public sealed class PreparedLeaf
    {
        [ProtoMember(1)] public ulong Guid { get; set; }
        [ProtoMember(2)] public float? Value { get; set; }
    }

    [ProtoContract]
    public sealed class PreparedItem
    {
        [ProtoMember(1)] public uint Id { get; set; }
        [ProtoMember(2)] public string? Name { get; set; }
        [ProtoMember(3)] public List<PreparedLeaf>? Leaves { get; set; }
        [ProtoMember(4)] public PreparedKind Kind { get; set; }
    }

    /// <summary>The shapes server packets have: scalars, a list of scalars, a message, and a list of messages that nest.</summary>
    [ProtoContract]
    public sealed class PreparedPacket : Packet
    {
        [ProtoMember(1)] public long Number { get; set; }
        [ProtoMember(2)] public string? Text { get; set; }
        [ProtoMember(3)] public List<ulong>? Numbers { get; set; }
        [ProtoMember(4)] public PreparedItem? Item { get; set; }
        [ProtoMember(5)] public List<PreparedItem>? Items { get; set; }
        [ProtoMember(6)] public DateTime When { get; set; }
        [ProtoMember(7)] public byte[]? Bytes { get; set; }
    }

    /// <summary>
    /// A packet protobuf-net cannot build a contract for: two of its members claim one field number. Its analyzer
    /// says so as this is compiled, which is silenced here; a contract it does not catch fails as this one does.
    /// </summary>
    [ProtoContract]
    public sealed class UnbuildablePacket : Packet
    {
#pragma warning disable PBN0003
        [ProtoMember(1)] public int First { get; set; }
        [ProtoMember(1)] public int Second { get; set; }
#pragma warning restore PBN0003
    }

    /// <summary>A packet with a member of a shape no packet has yet, which the preparation has no value to write.</summary>
    [ProtoContract]
    public sealed class UnfillablePacket : Packet
    {
        [ProtoMember(1)] public PreparedItem? Item { get; set; }
        [ProtoMember(2)] public Dictionary<int, string>? Lookup { get; set; }
    }

    /// <summary>
    /// A packet that could never be sent stops the start, with its name: left to the first send, its contract fails
    /// every send of the type, on the tick. So does one the preparation cannot write in full, since what it left
    /// out would be built by the first packet that carries it.
    /// </summary>
    [Theory]
    [InlineData(typeof(UnbuildablePacket))]
    [InlineData(typeof(UnfillablePacket))]
    public void Refuse_to_prepare_a_packet_it_cannot_build_everything_for_and_name_it(Type packetType)
    {
        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => PacketEncoder.PrepareServerPacket(packetType));

        Assert.Contains(packetType.Name, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Once a type is prepared, encoding its first packet allocates what encoding every later one does, which into
    /// a pooled segment is nothing: no serializer is left to build for the message or for a message inside it. Left
    /// to the first send, protobuf-net builds them then: some 360 KB of allocations for this packet, and 100 KB when
    /// only a new message was written to prepare it, with nothing in the members that hold the messages inside.
    /// </summary>
    [Fact]
    public void Leave_nothing_to_build_for_the_first_packet_of_a_prepared_type()
    {
        var encoder = new PacketEncoder(new PayloadSegmentPool());
        PreparedPacket packet = Filled();
        // Before the window: the thread's stream, through a type already built, and a segment of this packet's size.
        encoder.Encode(new SPingPacket { ServerTimestamp = long.MaxValue, ClientTimestamp = long.MaxValue },
            NetworkPacketType.SMSG_PING, NetworkPacketFlags.None, NetworkProtocol.Tcp).Release();
        new OutboundPacket(default, encoder.Pool.Rent(new byte[PreparedPayloadLength])).Release();

        PacketEncoder.PrepareServerPacket(typeof(PreparedPacket));
        // What each thread makes once for a type, and no startup can make for it: its own scratch message.
        _ = PacketEncoder.Scratch<PreparedPacket>();

        GC.Collect(0, GCCollectionMode.Forced, blocking: true); // the thread's allocation buffer, so the count is exact
        long before = GC.GetAllocatedBytesForCurrentThread();
        OutboundPacket first = encoder.Encode(packet, NetworkPacketType.SMSG_PING, NetworkPacketFlags.None, NetworkProtocol.Tcp);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(PreparedPayloadLength, first.PayloadLength);
        first.Release();
        Assert.Equal(0, allocated);
    }

    /// <summary>What <see cref="Filled" /> encodes to: the segment taken before the window must be of its size class.</summary>
    private const int PreparedPayloadLength = 73;

    private static PreparedPacket Filled() => new()
    {
        Number = 7,
        Text = "text",
        Numbers = [1, 2, 3],
        Item = new PreparedItem { Id = 1, Name = "one", Kind = PreparedKind.Some, Leaves = [new PreparedLeaf { Guid = 9, Value = 1.5f }] },
        Items =
        [
            new PreparedItem { Id = 2, Name = "two", Leaves = [new PreparedLeaf { Guid = 10 }, new PreparedLeaf { Guid = 11, Value = 2f }] },
            new PreparedItem { Id = 3 },
        ],
        When = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc),
        Bytes = [1, 2, 3],
    };
}
