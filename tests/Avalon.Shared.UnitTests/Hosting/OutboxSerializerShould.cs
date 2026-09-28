using System;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Hosting;

/// <summary>
/// #640: every flush frames each queued packet through the outbox's own reused writer. A packet holds
/// one nested message (its header) and a payload, which protobuf-net writes to a buffer writer without
/// allocating, unlike the lists of nested messages inside a state broadcast. Pinned so it stays that way.
/// </summary>
public class OutboxSerializerShould
{
    private static NetworkPacket Packet(int payloadLength) => new()
    {
        Header = new NetworkPacketHeader
        {
            Type = NetworkPacketType.SMSG_WORLD_STATE_UPDATE,
            Flags = NetworkPacketFlags.Encrypted,
            Protocol = NetworkProtocol.Tcp,
            Version = 0,
        },
        Payload = new byte[payloadLength],
    };

    [Fact]
    public void Frame_a_packet_without_allocating()
    {
        using var burst = new PooledArrayBufferWriter();
        using var scratch = new PooledArrayBufferWriter();
        NetworkPacket packet = Packet(7_000);
        OutboxSerializer.AppendPacket(burst, scratch, packet);

        // The fewest bytes over three windows, as WaypointRepathAllocationShould takes them.
        long fewest = long.MaxValue;
        for (int window = 0; window < 3; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int flush = 0; flush < 100; flush++)
            {
                burst.Reset();
                OutboxSerializer.AppendPacket(burst, scratch, packet);
            }

            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, fewest);
    }

    public static TheoryData<int> PayloadLengths() => new() { 0, 1, 100, 127, 128, 7_000, 70_000 };

    [Theory]
    [MemberData(nameof(PayloadLengths))]
    public void Frame_the_bytes_a_buffer_writer_frames(int payloadLength)
    {
        NetworkPacket packet = Packet(payloadLength);
        new Random(payloadLength).NextBytes(packet.Payload);

        using var reference = new PooledArrayBufferWriter();
        using var body = new PooledArrayBufferWriter();
        Serializer.Serialize(body, packet);
        OutboxSerializer.WriteVarint(reference, (uint)body.Written);
        body.WrittenSpan.CopyTo(reference.GetSpan(body.Written));
        reference.Advance(body.Written);

        // After a larger packet, so the reused writer must start each one afresh.
        using var burst = new PooledArrayBufferWriter();
        using var scratch = new PooledArrayBufferWriter();
        OutboxSerializer.AppendPacket(burst, scratch, Packet(80_000));
        burst.Reset();
        OutboxSerializer.AppendPacket(burst, scratch, packet);

        Assert.Equal(reference.WrittenSpan.ToArray(), burst.WrittenSpan.ToArray());
    }
}
