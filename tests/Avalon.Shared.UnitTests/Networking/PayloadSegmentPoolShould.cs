using Avalon.Network.Packets.Abstractions;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class PayloadSegmentPoolShould
{
    /// <summary>
    /// The pool is the process's, and a burst can leave it holding many free segments of a size. Taking and giving back
    /// segments it already holds must then cost nothing, however many it holds. A <c>ConcurrentQueue</c> free list, after
    /// a burst, kept allocating new, larger internal segments with no new peak until its free segments sat in one; those
    /// allocations landed in later ticks (here, one 32,768-slot segment: 524,544 B). A stack allocates only at a new peak.
    /// </summary>
    [Fact]
    public void Allocate_nothing_to_hand_out_and_take_back_segments_it_holds_after_a_burst()
    {
        var pool = new PayloadSegmentPool();
        byte[] payload = new byte[40];

        // The burst: many in flight at once, then all given back.
        var burst = new OutboundPacket[20_000];
        for (int i = 0; i < burst.Length; i++)
            burst[i] = new OutboundPacket(default, pool.Rent(payload));
        foreach (OutboundPacket packet in burst)
            packet.Release();

        var inFlight = new OutboundPacket[64];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 1_000; round++)
        {
            for (int i = 0; i < inFlight.Length; i++)
                inFlight[i] = new OutboundPacket(default, pool.Rent(payload));
            for (int i = 0; i < inFlight.Length; i++)
                inFlight[i].Release();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>
    /// A blip that left a thousand connections each holding their pending bytes must not pin those segments for good: a
    /// size class keeps at most <see cref="PayloadSegmentPool.MaxFreeBytesPerSizeClass" /> of free segments, but never
    /// fewer than <see cref="PayloadSegmentPool.MinFreeSegmentsPerSmallSizeClass" /> up to 4 KiB, whether they come back
    /// one by one or in a send pass's batch, and the rest go to the GC.
    /// </summary>
    [Theory]
    [InlineData(40, false, 65_536)] // the 64 B class: its byte cap
    [InlineData(40, true, 65_536)]
    [InlineData(4096, true, 2048)] // the 4 KiB class: the floor, above its byte cap's 1,024
    [InlineData(8192, false, 512)] // the 8 KiB class: past the floor's reach, its byte cap
    [InlineData(1024 * 1024, false, 4)] // the 1 MiB class
    public void Keep_no_more_free_segments_of_a_size_than_its_cap(int payloadLength, bool batched, int kept)
    {
        var pool = new PayloadSegmentPool(countOutstanding: true);
        byte[] payload = new byte[payloadLength];

        var burst = new OutboundPacket[kept + 3];
        var given = new HashSet<PayloadSegment>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < burst.Length; i++)
        {
            burst[i] = new OutboundPacket(default, pool.Rent(payload));
            given.Add(burst[i].Payload!);
        }

        if (batched)
            pool.BeginReturnBatch();
        foreach (OutboundPacket packet in burst)
            packet.Release();
        if (batched)
            pool.EndReturnBatch();

        Assert.Equal(0, pool.Outstanding);
        int reused = 0;
        for (int i = 0; i < burst.Length; i++)
        {
            if (given.Contains(pool.Rent(payload)))
                reused++;
        }

        Assert.Equal(kept, reused);
    }
}
