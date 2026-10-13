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

    /// <summary>
    /// Three marks make three packets of one shape, each with message types of its own inside: three first sends,
    /// each of a type nothing else has built.
    /// </summary>
    public sealed class First;

    public sealed class Second;

    public sealed class Third;

    [ProtoContract]
    public sealed class PreparedLeaf<TMark>
    {
        [ProtoMember(1)] public ulong Guid { get; set; }
        [ProtoMember(2)] public float? Value { get; set; }

        public static PreparedLeaf<TMark> Of(ulong guid, float? value = null) => new() { Guid = guid, Value = value };
    }

    [ProtoContract]
    public sealed class PreparedItem<TMark>
    {
        [ProtoMember(1)] public uint Id { get; set; }
        [ProtoMember(2)] public string? Name { get; set; }
        [ProtoMember(3)] public List<PreparedLeaf<TMark>>? Leaves { get; set; }
        [ProtoMember(4)] public PreparedKind Kind { get; set; }
    }

    /// <summary>The shapes server packets have: scalars, a list of scalars, a message, and a list of messages that nest.</summary>
    [ProtoContract]
    public sealed class PreparedPacket<TMark> : Packet
    {
        [ProtoMember(1)] public long Number { get; set; }
        [ProtoMember(2)] public string? Text { get; set; }
        [ProtoMember(3)] public List<ulong>? Numbers { get; set; }
        [ProtoMember(4)] public PreparedItem<TMark>? Item { get; set; }
        [ProtoMember(5)] public List<PreparedItem<TMark>>? Items { get; set; }
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
        [ProtoMember(1)] public PreparedItem<First>? Item { get; set; }
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
    /// <remarks>
    /// A first send happens once, so one cannot be measured three times: three packets of the same shape are, each
    /// with types of its own, and the fewest bytes of the three count. One window alone failed about once in thirty
    /// runs of this project: the runtime allocates some things once in a process, on whichever thread first needs
    /// them (24,648 B was seen), and that thread was sometimes this one, inside the window. It cannot be in all three.
    /// </remarks>
    [Fact]
    public void Leave_nothing_to_build_for_the_first_packet_of_a_prepared_type()
    {
        var encoder = new PacketEncoder(new PayloadSegmentPool());
        // Before any window: the thread's stream, through a type already built, and a segment of the packets' size.
        encoder.Encode(new SPingPacket { ServerTimestamp = long.MaxValue, ClientTimestamp = long.MaxValue },
            NetworkPacketType.SMSG_PING, NetworkPacketFlags.None, NetworkProtocol.Tcp).Release();
        new OutboundPacket(default, encoder.Pool.Rent(new byte[PreparedPayloadLength])).Release();

        long[] allocated = [FirstSend<First>(encoder), FirstSend<Second>(encoder), FirstSend<Third>(encoder)];

        Assert.True(allocated.Min() == 0,
            $"The first packets of three prepared types allocated {string.Join(", ", allocated)} B.");
    }

    /// <summary>What <see cref="Filled{TMark}" /> encodes to: the segment taken before the windows must be of its size class.</summary>
    private const int PreparedPayloadLength = 73;

    /// <summary>Prepares the packet of <typeparamref name="TMark" /> and answers what encoding its first packet allocated.</summary>
    private static long FirstSend<TMark>(PacketEncoder encoder)
    {
        PreparedPacket<TMark> packet = Filled<TMark>();
        PacketEncoder.PrepareServerPacket(typeof(PreparedPacket<TMark>));
        // What each thread makes once for a type, and no startup can make for it: its own scratch message.
        _ = PacketEncoder.Scratch<PreparedPacket<TMark>>();

        GC.Collect(0, GCCollectionMode.Forced, blocking: true); // the thread's allocation buffer, so the count is exact
        long before = GC.GetAllocatedBytesForCurrentThread();
        OutboundPacket first = encoder.Encode(packet, NetworkPacketType.SMSG_PING, NetworkPacketFlags.None, NetworkProtocol.Tcp);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(PreparedPayloadLength, first.PayloadLength);
        first.Release();
        return allocated;
    }

    private static PreparedPacket<TMark> Filled<TMark>() => new()
    {
        Number = 7,
        Text = "text",
        Numbers = [1, 2, 3],
        Item = new PreparedItem<TMark> { Id = 1, Name = "one", Kind = PreparedKind.Some, Leaves = [PreparedLeaf<TMark>.Of(9, 1.5f)] },
        Items =
        [
            new PreparedItem<TMark> { Id = 2, Name = "two", Leaves = [PreparedLeaf<TMark>.Of(10), PreparedLeaf<TMark>.Of(11, 2f)] },
            new PreparedItem<TMark> { Id = 3 },
        ],
        When = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc),
        Bytes = [1, 2, 3],
    };
}
