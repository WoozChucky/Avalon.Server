using Avalon.Network.Packets.Abstractions;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

public class PayloadSegmentPoolShould
{
    /// <summary>
    /// The pool is the process's, and a burst can leave it holding many free segments of a size. Taking and giving back
    /// segments it already holds must then cost nothing, however many it holds: a free list that moved through new
    /// internal storage with every segment taken and given back made each later tick allocate in proportion to its sends.
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
}
