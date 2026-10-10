using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.State;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Serialization;

/// <summary>
/// #640: the state broadcast serializes a message per entity in view, each holding a position and a
/// velocity message, every tenth of a second for every player. Written through a buffer writer,
/// protobuf-net measures every nested message ahead of writing it and allocates about 250 bytes per
/// entity to do so, which was most of what a busy tick allocated. The bytes on the wire must not change.
/// </summary>
public class PacketSerializationAllocationShould
{
    /// <summary>An encoder over a pool of its own: each payload released goes back to it for the next packet.</summary>
    private static readonly PacketEncoder s_encoder = new(new PayloadSegmentPool());

    private static readonly DateTime s_when = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static List<ObjectState> States(int count, string name)
    {
        var states = new List<ObjectState>(count);
        for (int i = 0; i < count; i++)
        {
            states.Add(new ObjectState
            {
                Guid = 10_000UL + (ulong)i,
                Position = new Vec3 { X = i, Y = 0.5f, Z = -i },
                Velocity = new Vec3 { X = 5f, Y = 0f, Z = -5f },
                Orientation = 90f,
                MoveState = MoveState.Running,
                CurrentHealth = 100,
                CreatureMetadataId = 4,
                Name = name,
                CanInteract = i % 2 == 0 ? true : null,
            });
        }

        return states;
    }

    [Fact]
    public void Serialize_nested_messages_without_allocating_per_message()
    {
        var packet = new SInstanceStateUpdatePacket { Updates = States(100, "Bench Wolf") };
        SInstanceStateUpdatePacket.Create(packet.Updates, s_encoder).Release();

        // The fewest bytes over three windows, as WaypointRepathAllocationShould takes them.
        long fewest = long.MaxValue;
        for (int window = 0; window < 3; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int create = 0; create < 10; create++)
                SInstanceStateUpdatePacket.Create(packet.Updates, s_encoder).Release();

            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // What remains is what every packet costs, however much it holds: the message and what encoding it
        // takes. The payload's segment goes back to the pool for the next one.
        Assert.InRange(fewest / 10, 0, 256);
    }

    public static TheoryData<int, int> Shapes() => new()
    {
        { 0, 0 },
        { 1, 5 },
        { 100, 10 },
        // Names past 127 bytes give each entity a two-byte length, and the whole a three-byte one.
        { 400, 200 },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Write_the_bytes_a_buffer_writer_writes(int count, int nameLength)
    {
        var packet = new SInstanceStateUpdatePacket { Updates = States(count, new string('w', nameLength)) };
        using var reference = new PooledArrayBufferWriter();
        Serializer.Serialize(reference, packet);

        OutboundPacket written = SInstanceStateUpdatePacket.Create(packet.Updates, s_encoder);

        Assert.Equal(reference.WrittenSpan.ToArray(), written.PayloadMemory.ToArray());
        written.Release();
    }

    [Fact]
    public void Write_a_flat_message_as_a_buffer_writer_writes()
    {
        var packet = new SChatMessagePacket
        {
            AccountId = 42,
            CharacterId = 7,
            CharacterName = "Alice",
            Message = new string('m', 300),
            DateTime = s_when,
        };
        using var reference = new PooledArrayBufferWriter();
        Serializer.Serialize(reference, packet);

        OutboundPacket written = SChatMessagePacket.Create(42, 7, "Alice", new string('m', 300), s_when, s_encoder);

        Assert.Equal(reference.WrittenSpan.ToArray(), written.PayloadMemory.ToArray());
        written.Release();
    }

    private static List<ObjectGuid> Guids(int count)
    {
        var guids = new List<ObjectGuid>(count);
        for (uint i = 0; i < count; i++)
            guids.Add(new ObjectGuid(ObjectType.Creature, 10_000 + i));
        return guids;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void Write_a_remove_as_its_ids_would_be_written(int count)
    {
        List<ObjectGuid> guids = Guids(count);
        var expected = new SInstanceStateRemovePacket { Removes = guids.ConvertAll(g => g.RawValue) };
        using var reference = new PooledArrayBufferWriter();
        Serializer.Serialize(reference, expected);

        OutboundPacket written = SInstanceStateRemovePacket.Create(guids, s_encoder);

        Assert.Equal(reference.WrittenSpan.ToArray(), written.PayloadMemory.ToArray());
        written.Release();
    }

    /// <summary>The id list is built at its final size in one pass, with no LINQ iterator or regrowth.</summary>
    [Fact]
    public void Build_a_remove_list_at_its_size()
    {
        List<ObjectGuid> guids = Guids(100);
        SInstanceStateRemovePacket.Create(guids, s_encoder).Release();

        long fewest = long.MaxValue;
        for (int window = 0; window < 3; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            SInstanceStateRemovePacket.Create(guids, s_encoder).Release();
            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // Only what the packet holds: the id list (32 bytes) and its array of 100 ulongs (824), plus the
        // packet itself (120 allowed). LINQ added its iterator on top, 1,048 in all.
        Assert.InRange(fewest, 0, 32 + 824 + 120);
    }

    [Fact]
    public void Start_each_packet_afresh_after_a_larger_one()
    {
        SInstanceStateUpdatePacket.Create(States(400, new string('w', 200)), s_encoder).Release();

        List<ObjectState> small = States(1, "Wolf");
        var packet = new SInstanceStateUpdatePacket { Updates = small };
        using var reference = new PooledArrayBufferWriter();
        Serializer.Serialize(reference, packet);

        OutboundPacket written = SInstanceStateUpdatePacket.Create(small, s_encoder);

        Assert.Equal(reference.WrittenSpan.ToArray(), written.PayloadMemory.ToArray());
        written.Release();
    }
}
