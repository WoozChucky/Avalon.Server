using System.Diagnostics.Metrics;
using Avalon.Configuration;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Movement;
using Avalon.World;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

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
        const int Count = 20_000;
        // The producer runs flat out, which the depth cap (#927) exists to end: here the cap is out of its reach.
        using var kicks = new KickProbe(Count);
        using var fixture = new ReceiveQueueFixture(receiveQueueLimit: kicks.Limit);
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

    /// <summary>
    /// The depth cap (#927): a packet that arrives with the cap already waiting is not queued and closes the
    /// connection, once, however many follow it. A head the tick holds is still waiting, so a connection wedged
    /// behind one (see ProcessQueueWedgeShould) is closed at the cap too, rather than left to grow.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Close_a_connection_that_sends_past_the_cap_once_and_queue_nothing_more(bool behindAHeldHead)
    {
        var log = new TestLog();
        using var kicks = new KickProbe(new NetworkConfiguration().MaxReceiveQueueDepth);
        using var fixture = new ReceiveQueueFixture(receiveQueueLimit: kicks.Limit, loggerFactory: log);
        int cap = kicks.Limit.MaxDepth;
        var input = new CPlayerInputPacket();
        int waiting = 0;
        if (behindAHeldHead)
        {
            // Queued before the spawn and refused by both passes after it.
            ICharacter character = fixture.Connection.Character!;
            fixture.Connection.Character = null;
            fixture.Connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LIST, new CCharacterListPacket());
            fixture.Connection.Character = character;
            fixture.Pump();
            waiting = 1;
        }

        for (; waiting < cap; waiting++)
            fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, input);
        Assert.False(fixture.Connection.IsClosing);
        Assert.Equal(cap, fixture.QueueDepth);

        for (int past = 0; past < 100; past++)
            fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, input);

        Assert.True(fixture.Connection.IsClosing);
        Assert.Equal(cap, fixture.QueueDepth);
        Assert.Equal(1, kicks.Count);
        Assert.Single(log.Entries, entry => entry.Level == LogLevel.Warning);

        // What was waiting is dropped, as on any close.
        fixture.Pump();
        Assert.Equal(0, fixture.Dispatched);
        Assert.Equal(0, fixture.QueueDepth);
    }

    /// <summary>
    /// The depth is counted, so a count that drifted would close an honest connection after hours, or stop closing
    /// anything. Every round fills the queue to the cap exactly, which is the largest burst accepted whole, and lets
    /// the tick empty it by one of its three ways out: dispatched, dispatched around a head held between the two
    /// passes, and dropped as left behind by a character that has gone. One packet counted as still waiting closes
    /// the next round; one counted out twice fails the next round's depth.
    /// </summary>
    [Fact]
    public void Never_close_a_connection_the_tick_empties_however_much_it_sends()
    {
        const int Cap = 64;
        const int Rounds = 60;
        using var kicks = new KickProbe(Cap);
        using var fixture = new ReceiveQueueFixture(receiveQueueLimit: kicks.Limit);
        ICharacter character = fixture.Connection.Character!;
        var input = new CPlayerInputPacket();
        var loaded = new CCharacterLoadedPacket();
        int dispatched = 0;

        for (int round = 0; round < Rounds; round++)
        {
            bool alternate = round % 3 == 1, leftBehind = round % 3 == 2;
            for (int packet = 0; packet < Cap; packet++)
            {
                if (alternate && packet % 2 == 1)
                    fixture.Connection.Deliver(NetworkPacketType.CMSG_CHARACTER_LOADED, loaded);
                else
                    fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, input);
            }

            Assert.Equal(Cap, fixture.QueueDepth);
            if (leftBehind)
                fixture.Connection.Character = null;
            else
                dispatched += Cap;
            for (int tick = 0; tick < Cap && fixture.QueueDepth != 0; tick++)
                fixture.Pump();
            Assert.Equal(0, fixture.QueueDepth);
            fixture.Connection.Character = character;
        }

        Assert.False(fixture.Connection.IsClosing);
        Assert.Equal(dispatched, fixture.Dispatched);
        Assert.Equal(0, kicks.Count);

        // And the cap still stands where it did.
        for (int packet = 0; packet < Cap; packet++)
            fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, input);
        Assert.False(fixture.Connection.IsClosing);
        fixture.Connection.Deliver(NetworkPacketType.CMSG_PLAYER_INPUT, input);
        Assert.True(fixture.Connection.IsClosing);
    }

    /// <summary>A depth cap whose kicks (<c>network.in.backlog_kicks</c>) are counted on a meter of the test's own.</summary>
    private sealed class KickProbe : IDisposable
    {
        private readonly Meter _meter = new($"test-{Guid.NewGuid()}");
        private readonly MeterListener _listener = new();
        private long _count;

        public KickProbe(int maxDepth)
        {
            Limit = new ReceiveQueueLimit(maxDepth, _meter);
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter == _meter && instrument.Name == "network.in.backlog_kicks")
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref _count, value));
            _listener.Start();
        }

        public ReceiveQueueLimit Limit { get; }
        public long Count => Interlocked.Read(ref _count);

        public void Dispose()
        {
            _listener.Dispose();
            _meter.Dispose();
        }
    }
}
