using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Movement;

namespace Avalon.Server.World.UnitTests.Filters;

public class ReceiveQueueShould
{
    [Fact]
    public void Keep_the_filtered_head_ahead_of_later_packets_with_its_original_arrival_time()
    {
        using var fixture = new ReceiveQueueFixture();
        var dispatched = new List<(Packet Packet, long Arrived)>();
        fixture.OnDispatch = (connection, packet) => dispatched.Add((packet, connection.CurrentPacketArrivedTicks));
        var input = new CPlayerInputPacket();
        var loaded = new CCharacterLoadedPacket();
        long inputArrived = fixture.Clock.Now.UtcTicks;
        fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, input);
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(1);
        long loadedArrived = fixture.Clock.Now.UtcTicks;
        fixture.Connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LOADED, loaded);

        fixture.Connection.UpdateSession();
        Assert.Empty(dispatched);
        Assert.Equal(2, fixture.QueueDepth);
        fixture.Connection.UpdateMap();
        Assert.Equal((input, inputArrived), Assert.Single(dispatched));
        Assert.Equal(1, fixture.QueueDepth);
        fixture.Connection.UpdateSession();
        Assert.Equal(new (Packet, long)[] { (input, inputArrived), (loaded, loadedArrived) }, dispatched);
        Assert.Equal(0, fixture.QueueDepth);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Discard_the_filtered_head_and_following_packets_when_authorization_ends(bool maintenance)
    {
        using var fixture = new ReceiveQueueFixture();
        fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket());
        fixture.Connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LOADED, new CCharacterLoadedPacket());
        fixture.Connection.UpdateSession();
        Assert.Equal(2, fixture.QueueDepth);

        if (maintenance)
            fixture.Connection.BlockForMaintenance();
        else
            fixture.Connection.GameSessionLease!.Revoke();
        fixture.Connection.UpdateMap();
        fixture.Connection.UpdateSession();

        Assert.Equal(0, fixture.Dispatched);
        Assert.Equal(0, fixture.QueueDepth);
    }

    [Fact]
    public async Task Preserve_fifo_when_arrivals_and_the_two_tick_passes_run_on_different_threads()
    {
        using var fixture = new ReceiveQueueFixture();
        const int Count = 20_000;
        var dispatched = new List<uint>(Count);
        fixture.OnDispatch = (_, packet) => dispatched.Add(((CPlayerInputPacket)packet).Seq);
        var producer = Task.Run(() =>
        {
            for (uint i = 0; i < Count; i++)
                fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, new CPlayerInputPacket { Seq = i });
        });
        // One consumer owns both tick passes; the producer only delivers packets.
        await Task.Run(() =>
        {
            while (!producer.IsCompleted || fixture.QueueDepth != 0)
                fixture.Pump();
        }).WaitAsync(TimeSpan.FromSeconds(10));
        await producer;

        Assert.Equal(Enumerable.Range(0, Count).Select(i => (uint)i), dispatched);
    }
}
