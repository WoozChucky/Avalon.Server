using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// A connection's time sync (#892). The round trip is the server's own measurement: from the stamp its send thread put
/// on a ping as it wrote it, to the arrival of the pong that echoes that stamp. The echo only says which ping a pong
/// answers, each ping written lets one pong in, and the client's own times place its clock and nothing else. Every case
/// runs an admitted connection with its real sender and pong handler: each ping is read off the wire as a client reads
/// it, each pong arrives as the read loop delivers it and is handled by the tick's session pass, and the test drives
/// the clock.
/// </summary>
public sealed class WorldConnectionTimeSyncShould : IDisposable
{
    private const long TicksPerMs = TimeSpan.TicksPerMillisecond;
    private const long TicksPerSecond = TimeSpan.TicksPerSecond;

    private readonly MemoryStream _wire = new();
    private readonly ReceiveQueueFixture _fixture;

    public WorldConnectionTimeSyncShould() => _fixture = new ReceiveQueueFixture(_wire);

    private Avalon.World.WorldConnection Connection => _fixture.Connection;

    public void Dispose() => _fixture.Dispose();

    private void Advance(TimeSpan by) => _fixture.Clock.Now += by;

    /// <summary>The tick queues a ping and its send thread writes it now: the ping as the client reads it.</summary>
    private Task<SPingPacket> WritePingAsync()
    {
        Connection.SendTimeSyncPing();
        return WriteQueuedPingAsync();
    }

    /// <summary>The send thread's pass, now: the ping it writes, as the client reads it.</summary>
    private async Task<SPingPacket> WriteQueuedPingAsync()
    {
        _fixture.Scheduler!.RunAllPasses();

        SPingPacket? newest = null;
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await foreach (ReadOnlyMemory<byte> raw in new PacketStream(new MemoryStream(_wire.ToArray()))
            .EnumerateRawFramesAsync(256, int.MaxValue, guard.Token))
        {
            var frame = InboundPacketFrame.ParseFrame(raw);
            Assert.Equal(NetworkPacketType.SMSG_PING, frame.Header.Type);
            newest = Serializer.Deserialize<SPingPacket>(frame.Payload);
        }

        return newest ?? throw new InvalidOperationException("No ping was written");
    }

    /// <summary>The read loop delivers a pong at <paramref name="arrivesAt" /> on the server's clock.</summary>
    private void Arrive(long echo, long clientReceived, long clientSent, long arrivesAt)
    {
        _fixture.Clock.Now = new DateTimeOffset(arrivesAt, TimeSpan.Zero);
        _fixture.Connection.Deliver(NetworkPacketType.CMSG_PONG, new CPongPacket
        {
            LastServerTimestamp = echo,
            ClientReceivedTimestamp = clientReceived,
            ClientSentTimestamp = clientSent,
        });
    }

    /// <summary>A pong arrives and the tick's next pass handles it, if it was let in.</summary>
    private void Pong(long echo, long clientReceived, long clientSent, long arrivesAt)
    {
        Arrive(echo, clientReceived, clientSent, arrivesAt);
        _fixture.Pump();
    }

    /// <summary>
    /// A whole honest exchange: <paramref name="oneWayTicks" /> out and as long back, answered the instant it lands, by a
    /// client whose clock runs <paramref name="clientAheadTicks" /> ahead of the server's.
    /// </summary>
    private async Task<long> ExchangeAsync(long oneWayTicks, long clientAheadTicks = 0)
    {
        long stamp = (await WritePingAsync()).ServerTimestamp;
        long clientTouchedAt = stamp + oneWayTicks + clientAheadTicks;
        Pong(stamp, clientTouchedAt, clientTouchedAt, stamp + 2 * oneWayTicks);
        return stamp;
    }

    /// <summary>
    /// The pong lands 20 ms after its ping was written, and that is the round trip, whatever receive and send times the
    /// client puts in it: an honest client's, one that took 5 ms to answer (no longer taken off), and three that the
    /// formula over the client's times read as no round trip, a negative one and an hour.
    /// </summary>
    [Theory]
    [InlineData(10 * TicksPerMs, 10 * TicksPerMs)]
    [InlineData(10 * TicksPerMs, 15 * TicksPerMs)]
    [InlineData(0L, 20 * TicksPerMs)]
    [InlineData(-TicksPerSecond, TicksPerSecond)]
    [InlineData(TimeSpan.TicksPerHour, 0L)]
    public async Task MeasureTheRoundTripItself_WhateverTimesThePongCarries(long receivedAfterStamp, long sentAfterStamp)
    {
        long stamp = (await WritePingAsync()).ServerTimestamp;

        Pong(stamp, stamp + receivedAfterStamp, stamp + sentAfterStamp, stamp + 20 * TicksPerMs);

        Assert.Equal(20 * TicksPerMs, Connection.RoundTripTime);
        Assert.Equal(20, Connection.Latency);
    }

    /// <summary>
    /// A pong whose echo is the stamp of no ping (a tick either side of the one in flight, the next ping's likely stamp
    /// sent ahead of it, or zero, which an empty place in the record holds) changes nothing, and it was the ping's one
    /// answer: the true echo sent after it is dropped as it arrives. The next ping tells the client what the last
    /// answered one left, and is measured as any other.
    /// </summary>
    [Theory]
    [InlineData(1L, false)]
    [InlineData(-1L, false)]
    [InlineData(10 * TicksPerSecond, false)]
    [InlineData(0L, true)]
    public async Task ChangeNothing_AndSpendThePingsOneAnswer_WhenAPongEchoesTheStampOfNoPing(long echo, bool absolute)
    {
        long answeredAt = await ExchangeAsync(oneWayTicks: 10 * TicksPerMs, clientAheadTicks: TicksPerSecond)
            + 10 * TicksPerMs + TicksPerSecond;
        Advance(TimeSpan.FromSeconds(10));
        long inFlight = (await WritePingAsync()).ServerTimestamp;

        long guess = absolute ? echo : inFlight + echo;
        Pong(guess, guess, guess, inFlight + TicksPerMs);
        Pong(inFlight, inFlight + 15 * TicksPerMs, inFlight + 15 * TicksPerMs, inFlight + 30 * TicksPerMs);

        Assert.Equal(2, _fixture.Dispatched);   // the first exchange's pong and the guess; the true echo never got in
        Assert.Equal(20 * TicksPerMs, Connection.RoundTripTime);
        Assert.Equal(20, Connection.Latency);
        Assert.Equal(-TicksPerSecond, Connection.TimeSyncOffset);

        SPingPacket next = await WritePingAsync();
        Assert.Equal(20 * TicksPerMs, next.Rtt);
        Assert.Equal(-TicksPerSecond, next.Offset);
        Assert.Equal(answeredAt, next.ClientTimestamp);

        Pong(next.ServerTimestamp, next.ServerTimestamp, next.ServerTimestamp, next.ServerTimestamp + 50 * TicksPerMs);
        Assert.Equal(50 * TicksPerMs, Connection.RoundTripTime);
    }

    /// <summary>
    /// A ping is answered once. Of a burst of pongs after one ping, the first is let in and the rest are dropped as they
    /// arrive: never queued, never handled. And the first pong sent again once a later ping has let another pong in
    /// reaches the handler but is no second measurement: its stamp was taken.
    /// </summary>
    [Fact]
    public async Task AnswerEachPingOnce()
    {
        long stamp = (await WritePingAsync()).ServerTimestamp;

        Arrive(stamp, stamp + 10 * TicksPerMs, stamp + 10 * TicksPerMs, stamp + 20 * TicksPerMs);
        for (int extra = 1; extra <= 9; extra++)
            Arrive(stamp + extra % 3, stamp, stamp, stamp + (20 + extra) * TicksPerMs);
        Assert.Equal(1, _fixture.QueueDepth);
        _fixture.Pump();

        Assert.Equal(1, _fixture.Dispatched);
        Assert.Equal(20 * TicksPerMs, Connection.RoundTripTime);
        Assert.Equal(0, Connection.TimeSyncOffset);

        Advance(TimeSpan.FromSeconds(10));
        await WritePingAsync();
        Pong(stamp, stamp, stamp, stamp + 500 * TicksPerMs);

        Assert.Equal(2, _fixture.Dispatched);
        Assert.Equal(20 * TicksPerMs, Connection.RoundTripTime);
        Assert.Equal(20, Connection.Latency);
        Assert.Equal(0, Connection.TimeSyncOffset);
    }

    /// <summary>
    /// Two pings in flight, the handshake's beside a periodic one: each pong is timed from the stamp of the ping it
    /// answers, whichever lands first. The first ping's pong takes 100 ms, the second's 40 ms, and each took 5 ms of that
    /// to answer, which stays in the round trip.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeEachPong_FromItsOwnPing_WhenTwoAreInFlight(bool secondAnsweredFirst)
    {
        long first = (await WritePingAsync()).ServerTimestamp;
        Advance(TimeSpan.FromMilliseconds(30));
        long second = (await WritePingAsync()).ServerTimestamp;
        Assert.Equal(first + 30 * TicksPerMs, second);

        void AnswerFirst()
        {
            Pong(first, first + 50 * TicksPerMs, first + 55 * TicksPerMs, first + 100 * TicksPerMs);
            Assert.Equal(100 * TicksPerMs, Connection.RoundTripTime);
        }

        void AnswerSecond()
        {
            Pong(second, second + 20 * TicksPerMs, second + 25 * TicksPerMs, second + 40 * TicksPerMs);
            Assert.Equal(40 * TicksPerMs, Connection.RoundTripTime);
        }

        if (secondAnsweredFirst)
        {
            AnswerSecond();
            AnswerFirst();
        }
        else
        {
            AnswerFirst();
            AnswerSecond();
        }
    }

    /// <summary>
    /// The record of unanswered pings is a fixed size, and so is the number of pongs they let in. Two pings more than it
    /// holds, none answered: the two oldest stamps are forgotten, and only as many pongs get in as it holds stamps. A
    /// pong for a forgotten ping is ignored and uses one of those up, so the last of the recorded pings, still
    /// answerable by its stamp, is left without an answer.
    /// </summary>
    [Fact]
    public async Task KeepNoMoreStampsOrAnswers_ThanItRecords_WhenPingsGoUnanswered()
    {
        var stamps = new List<long>();
        for (int ping = 0; ping < ConnectionSender.PingStampCapacity + 2; ping++)
        {
            stamps.Add((await WritePingAsync()).ServerTimestamp);
            Advance(TimeSpan.FromSeconds(1));
        }

        long lastArrival = stamps[^1] + TicksPerSecond;
        Pong(stamps[1], stamps[1], stamps[1], lastArrival);
        Assert.Equal(1, _fixture.Dispatched);
        Assert.Equal(0, Connection.RoundTripTime);

        for (int ping = stamps.Count - 1; ping > 2; ping--)
        {
            lastArrival += TicksPerMs;
            Pong(stamps[ping], stamps[ping], stamps[ping], lastArrival);
            Assert.Equal(lastArrival - stamps[ping], Connection.RoundTripTime);
        }

        long roundTrip = Connection.RoundTripTime;
        Pong(stamps[2], stamps[2], stamps[2], lastArrival + TicksPerMs);
        Assert.Equal(ConnectionSender.PingStampCapacity, _fixture.Dispatched);
        Assert.Equal(roundTrip, Connection.RoundTripTime);
    }

    /// <summary>
    /// A pong that arrives "before" its ping was stamped measures nothing: no negative round trip, the last one stays.
    /// Here the server's wall clock, which both times come from, was set back under the exchange; an echo sent ahead of
    /// its ping reads the same.
    /// </summary>
    [Fact]
    public async Task KeepTheLastRoundTrip_WhenAPongArrivesBeforeItsPingWasStamped()
    {
        await ExchangeAsync(oneWayTicks: 10 * TicksPerMs);
        Advance(TimeSpan.FromSeconds(10));
        long stamp = (await WritePingAsync()).ServerTimestamp;

        Pong(stamp, stamp, stamp, stamp - TicksPerSecond);

        Assert.Equal(2, _fixture.Dispatched);
        Assert.Equal(20 * TicksPerMs, Connection.RoundTripTime);
        Assert.Equal(20, Connection.Latency);
        Assert.Equal(0, Connection.TimeSyncOffset);
    }

    /// <summary>
    /// The offset is server time minus client time, so a client running a second fast reads minus a second. Every term
    /// comes from the exchange being measured: the second exchange, ten seconds on, reads the same, where a term kept
    /// from the first would add the ten seconds between the pings.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(TicksPerSecond)]
    [InlineData(-TicksPerSecond)]
    public async Task ReportTheOffset_OfTheClientClock_FromEachExchangeAlone(long clientAheadTicks)
    {
        await ExchangeAsync(oneWayTicks: 10 * TicksPerMs, clientAheadTicks);
        Assert.Equal(-clientAheadTicks, Connection.TimeSyncOffset);

        Advance(TimeSpan.FromSeconds(10));
        await ExchangeAsync(oneWayTicks: 10 * TicksPerMs, clientAheadTicks);
        Assert.Equal(-clientAheadTicks, Connection.TimeSyncOffset);
    }

    /// <summary>
    /// The pong lands on the socket two milliseconds after its ping left and is handled a full 60 Hz tick later. The round
    /// trip ends at the arrival, not at the handling, so the wait for the tick is charged to neither it nor the offset.
    /// </summary>
    [Fact]
    public async Task ChargeNoneOfTheTickWait_WhenThePongWaitedForOne()
    {
        long stamp = (await WritePingAsync()).ServerTimestamp;
        long clientTouchedAt = stamp + TicksPerMs;

        Arrive(stamp, clientTouchedAt, clientTouchedAt, arrivesAt: clientTouchedAt + TicksPerMs);
        Advance(TimeSpan.FromMilliseconds(16));   // handled a tick after it arrived
        _fixture.Pump();

        Assert.Equal(2 * TicksPerMs, Connection.RoundTripTime);
        Assert.Equal(0, Connection.TimeSyncOffset);
    }

    [Fact]
    public void HandOutAnInitialPingRequestOnce()
    {
        // The tick asks every connection every tick, so a request that did not clear would ping on
        // all of them -- once per 16 ms rather than once per ten seconds.
        Assert.False(Connection.TakeInitialTimeSyncPingRequest());

        Connection.RequestInitialTimeSyncPing();

        Assert.True(Connection.TakeInitialTimeSyncPingRequest());
        Assert.False(Connection.TakeInitialTimeSyncPingRequest());
    }

    /// <summary>
    /// The ping is stamped as its send thread writes it, not as the tick queues it (#875): a ping that waited 50 ms for
    /// its send thread carries the time it left, so the round trip holds none of that wait.
    /// </summary>
    [Fact]
    public async Task StampThePing_WhenItIsWritten()
    {
        long queuedAt = _fixture.Clock.Now.UtcTicks;
        Connection.SendTimeSyncPing();
        Advance(TimeSpan.FromMilliseconds(50));   // the ping waits for its send thread
        long stamp = (await WriteQueuedPingAsync()).ServerTimestamp;
        Assert.Equal(queuedAt + 50 * TicksPerMs, stamp);

        // The client has it a millisecond after it left and answers at once; the pong lands a millisecond later.
        // The round trip is those two milliseconds, not the 50 the ping spent queued.
        Pong(stamp, stamp + TicksPerMs, stamp + TicksPerMs, stamp + 2 * TicksPerMs);
        Assert.Equal(2 * TicksPerMs, Connection.RoundTripTime);
    }
}
