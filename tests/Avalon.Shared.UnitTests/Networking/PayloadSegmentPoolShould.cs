using Avalon.Network.Packets.Abstractions;
using Xunit;

namespace Avalon.Shared.UnitTests.Networking;

/// <summary>
/// A broadcast's payload is one pooled segment shared by every recipient (#875). Returned too early, the next packet
/// would be encoded over a payload still being sent; returned twice, two packets would share one buffer.
/// </summary>
public class PayloadSegmentPoolShould
{
    [Fact]
    public void Return_a_shared_segment_once_and_only_after_its_last_release()
    {
        var pool = new PayloadSegmentPool();
        var packet = new OutboundPacket(new NetworkPacketHeader(), pool.Rent(new byte[100]));
        OutboundPacket shared = packet.Share();

        packet.Release();
        PayloadSegment rentedMeanwhile = pool.Rent(new byte[100]);
        Assert.NotSame(shared.Payload, rentedMeanwhile);
        Assert.Equal(2, pool.Outstanding);

        shared.Release();
        Assert.Equal(1, pool.Outstanding);

        // Back in the pool exactly once: the next rent of its size takes it, the one after needs a new segment.
        Assert.Same(shared.Payload, pool.Rent(new byte[100]));
        Assert.NotSame(shared.Payload, pool.Rent(new byte[100]));
    }
}
