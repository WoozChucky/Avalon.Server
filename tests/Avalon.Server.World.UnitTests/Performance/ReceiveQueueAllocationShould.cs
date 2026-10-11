using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;

namespace Avalon.Server.World.UnitTests.Performance;

public class ReceiveQueueAllocationShould
{
    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(150)]
    public void Reuse_receive_queue_storage_after_warmup(int packetsPerTick)
    {
        using var fixture = new ReceiveQueueFixture();
        var input = new CPlayerInputPacket();
        Pump(fixture, input, packetsPerTick, 1_000);
        int dispatchedBefore = fixture.Dispatched;
        long before = GC.GetAllocatedBytesForCurrentThread();
        Pump(fixture, input, packetsPerTick, 1_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(packetsPerTick * 1_000, fixture.Dispatched - dispatchedBefore);
        Assert.Equal(0, allocated);
    }

    private static void Pump(ReceiveQueueFixture fixture, CPlayerInputPacket input, int packetsPerTick, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            for (int packet = 0; packet < packetsPerTick; packet++)
                fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, input);
            fixture.Pump();
        }
    }
}
