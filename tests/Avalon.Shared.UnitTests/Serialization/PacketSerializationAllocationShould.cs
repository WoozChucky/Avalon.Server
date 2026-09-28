using System;
using System.Collections.Generic;
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
    private static readonly byte[] Discarded = [];

    /// <summary>An encryption that keeps nothing, so only the serialization's own allocations count.</summary>
    private static readonly EncryptFunc Discard = static _ => Discarded;

    private static readonly EncryptFunc Identity = static span => span.ToArray();

    private static readonly DateTime When = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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
        SInstanceStateUpdatePacket.Create(packet.Updates, Discard);

        // The fewest bytes over three windows, as WaypointRepathAllocationShould takes them.
        long fewest = long.MaxValue;
        for (int window = 0; window < 3; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int create = 0; create < 10; create++)
                SInstanceStateUpdatePacket.Create(packet.Updates, Discard);

            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // What remains is what every packet costs, however much it holds: the packet, the
        // NetworkPacket and its header. The payload is the encryption's, and is discarded here.
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

        byte[] written = SInstanceStateUpdatePacket.Create(packet.Updates, Identity).Payload;

        Assert.Equal(reference.WrittenSpan.ToArray(), written);
    }

    [Fact]
    public void Write_a_flat_message_as_a_buffer_writer_writes()
    {
        var packet = new SChatMessagePacket
        {
            AccountId = 42, CharacterId = 7, CharacterName = "Alice", Message = new string('m', 300), DateTime = When,
        };
        using var reference = new PooledArrayBufferWriter();
        Serializer.Serialize(reference, packet);

        byte[] written = SChatMessagePacket.Create(42, 7, "Alice", new string('m', 300), When, Identity).Payload;

        Assert.Equal(reference.WrittenSpan.ToArray(), written);
    }

    [Fact]
    public void Start_each_packet_afresh_after_a_larger_one()
    {
        SInstanceStateUpdatePacket.Create(States(400, new string('w', 200)), Identity);

        List<ObjectState> small = States(1, "Wolf");
        var packet = new SInstanceStateUpdatePacket { Updates = small };
        using var reference = new PooledArrayBufferWriter();
        Serializer.Serialize(reference, packet);

        Assert.Equal(reference.WrittenSpan.ToArray(), SInstanceStateUpdatePacket.Create(small, Identity).Payload);
    }
}
