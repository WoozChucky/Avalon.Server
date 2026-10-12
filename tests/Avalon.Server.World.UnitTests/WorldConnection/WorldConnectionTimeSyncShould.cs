using System.Net;
using System.Net.Sockets;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// A connection's time sync (#892). The round trip is the server's own measurement: from the stamp its send thread put
/// on a ping as it wrote it, to the arrival of the pong that echoes that stamp. The echo only says which ping a pong
/// answers, each ping is answered once, and the client's own times place its clock and nothing else. Every case runs a
/// real connection and its sender, reads each ping off the wire as a client would, and drives the clock.
/// </summary>
public sealed class WorldConnectionTimeSyncShould : IDisposable
{
    private const long TicksPerMs = TimeSpan.TicksPerMillisecond;
    private const long TicksPerSecond = TimeSpan.TicksPerSecond;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly NetworkSendScheduler _scheduler;
    private readonly Avalon.World.WorldConnection _connection;
    private readonly TcpClient _serverSide;
    private readonly MemoryStream _wire = new();

    public WorldConnectionTimeSyncShould()
    {
        // Its send threads never start: each test runs the passes itself, so a ping is written when the test says.
        _scheduler = new NetworkSendScheduler(new NetworkConfiguration { SendThreads = 1 }, NullLoggerFactory.Instance,
            _clock, NetworkSendMetrics.Disabled);
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.SendScheduler.Returns(_scheduler);

        (TcpClient clientSide, TcpClient serverSide) = CreateLoopbackPair();
        _serverSide = serverSide;
        _connection = new Avalon.World.WorldConnection(server, clientSide, NullLoggerFactory.Instance,
            Substitute.For<IPacketReader>(), _clock);
        _connection.InitOutboxForTest(new PacketStream(_wire));
    }

    public void Dispose()
    {
        _connection.Close();
        _scheduler.RunAllPasses();   // the close's last visit, so the sender finishes and gives back its buffers
        _connection.Dispose();
        _scheduler.Dispose();
        _serverSide.Dispose();
    }

    private static (TcpClient clientSide, TcpClient serverSide) CreateLoopbackPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, port);
        TcpClient serverSide = listener.AcceptTcpClient();
        listener.Stop();
        return (clientSide, serverSide);
    }

    /// <summary>The tick queues a ping and its send thread writes it now: the ping as the client reads it.</summary>
    private Task<SPingPacket> WritePingAsync()
    {
        _connection.SendTimeSyncPing();
        return WriteQueuedPingAsync();
    }

    /// <summary>The send thread's pass, now: the ping it writes, as the client reads it.</summary>
    private async Task<SPingPacket> WriteQueuedPingAsync()
    {
        _scheduler.RunAllPasses();

        SPingPacket? newest = null;
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await foreach (ReadOnlyMemory<byte> raw in new PacketStream(new MemoryStream(_wire.ToArray()))
            .EnumerateRawFramesAsync(256, guard.Token))
        {
            var frame = InboundPacketFrame.ParseFrame(raw);
            Assert.Equal(NetworkPacketType.SMSG_PING, frame.Header.Type);
            newest = Serializer.Deserialize<SPingPacket>(frame.Payload);
        }

        return newest ?? throw new InvalidOperationException("No ping was written");
    }

    /// <summary>
    /// A whole honest exchange: <paramref name="oneWayTicks" /> out and as long back, answered the instant it lands, by a
    /// client whose clock runs <paramref name="clientAheadTicks" /> ahead of the server's.
    /// </summary>
    private async Task<long> ExchangeAsync(long oneWayTicks, long clientAheadTicks = 0)
    {
        long stamp = (await WritePingAsync()).ServerTimestamp;
        long clientTouchedAt = stamp + oneWayTicks + clientAheadTicks;
        _clock.Advance(TimeSpan.FromTicks(2 * oneWayTicks));
        _connection.OnPongReceived(stamp, clientTouchedAt, clientTouchedAt, stamp + 2 * oneWayTicks);
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

        _connection.OnPongReceived(stamp, stamp + receivedAfterStamp, stamp + sentAfterStamp, stamp + 20 * TicksPerMs);

        Assert.Equal(20 * TicksPerMs, _connection.RoundTripTime);
        Assert.Equal(20, _connection.Latency);
    }

    /// <summary>
    /// A pong whose echo is the stamp of no ping (a tick either side of the one in flight, the next ping's likely stamp
    /// sent ahead of it, or zero, which an empty place in the record holds) changes nothing, and the next ping tells the
    /// client what the last answered one left.
    /// </summary>
    [Theory]
    [InlineData(1L, false)]
    [InlineData(-1L, false)]
    [InlineData(10 * TicksPerSecond, false)]
    [InlineData(0L, true)]
    public async Task ChangeNothing_WhenAPongEchoesTheStampOfNoPing(long echo, bool absolute)
    {
        long answeredAt = await ExchangeAsync(oneWayTicks: 10 * TicksPerMs, clientAheadTicks: TicksPerSecond)
            + 10 * TicksPerMs + TicksPerSecond;
        _clock.Advance(TimeSpan.FromSeconds(10));
        long inFlight = (await WritePingAsync()).ServerTimestamp;

        long forged = absolute ? echo : inFlight + echo;
        _connection.OnPongReceived(forged, forged, forged, inFlight + TicksPerMs);

        Assert.Equal(20 * TicksPerMs, _connection.RoundTripTime);
        Assert.Equal(20, _connection.Latency);
        Assert.Equal(-TicksPerSecond, _connection.TimeSyncOffset);

        SPingPacket next = await WritePingAsync();
        Assert.Equal(20 * TicksPerMs, next.Rtt);
        Assert.Equal(-TicksPerSecond, next.Offset);
        Assert.Equal(answeredAt, next.ClientTimestamp);
    }

    /// <summary>A ping is answered once: the same pong again, arriving later, is not a second measurement.</summary>
    [Fact]
    public async Task IgnoreASecondPong_ForTheSamePing()
    {
        long stamp = (await WritePingAsync()).ServerTimestamp;
        _connection.OnPongReceived(stamp, stamp + 10 * TicksPerMs, stamp + 10 * TicksPerMs, stamp + 20 * TicksPerMs);

        _connection.OnPongReceived(stamp, stamp + 10 * TicksPerMs, stamp + 10 * TicksPerMs, stamp + 500 * TicksPerMs);
        _connection.OnPongReceived(stamp, stamp, stamp, stamp);

        Assert.Equal(20 * TicksPerMs, _connection.RoundTripTime);
        Assert.Equal(20, _connection.Latency);
        Assert.Equal(0, _connection.TimeSyncOffset);
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
        _clock.Advance(TimeSpan.FromMilliseconds(30));
        long second = (await WritePingAsync()).ServerTimestamp;
        Assert.Equal(first + 30 * TicksPerMs, second);

        void AnswerFirst()
        {
            _connection.OnPongReceived(first, first + 50 * TicksPerMs, first + 55 * TicksPerMs, first + 100 * TicksPerMs);
            Assert.Equal(100 * TicksPerMs, _connection.RoundTripTime);
        }

        void AnswerSecond()
        {
            _connection.OnPongReceived(second, second + 20 * TicksPerMs, second + 25 * TicksPerMs, second + 40 * TicksPerMs);
            Assert.Equal(40 * TicksPerMs, _connection.RoundTripTime);
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
    /// The record of unanswered pings is a fixed size: one ping more than it holds, none answered, and the oldest is the
    /// one forgotten. Its pong is ignored, and every later ping can still be answered.
    /// </summary>
    [Fact]
    public async Task ForgetTheOldestPing_WhenMoreAreUnansweredThanItRecords()
    {
        var stamps = new List<long>();
        for (int ping = 0; ping <= ConnectionSender.PingStampCapacity; ping++)
        {
            stamps.Add((await WritePingAsync()).ServerTimestamp);
            _clock.Advance(TimeSpan.FromSeconds(10));
        }

        _connection.OnPongReceived(stamps[0], stamps[0], stamps[0], stamps[0] + 20 * TicksPerMs);
        Assert.Equal(0, _connection.RoundTripTime);

        for (int ping = 1; ping < stamps.Count; ping++)
        {
            long roundTrip = (20 + ping) * TicksPerMs;
            _connection.OnPongReceived(stamps[ping], stamps[ping], stamps[ping], stamps[ping] + roundTrip);
            Assert.Equal(roundTrip, _connection.RoundTripTime);
        }
    }

    /// <summary>
    /// The pong's arrival comes from the server's wall clock, which can be set back under an exchange. A pong that
    /// arrives "before" its ping was written measures nothing: no negative round trip, the last one stays.
    /// </summary>
    [Fact]
    public async Task KeepTheLastRoundTrip_WhenAPongArrivesBeforeItsPingWasStamped()
    {
        await ExchangeAsync(oneWayTicks: 10 * TicksPerMs);
        _clock.Advance(TimeSpan.FromSeconds(10));
        long stamp = (await WritePingAsync()).ServerTimestamp;

        _connection.OnPongReceived(stamp, stamp, stamp, stamp - TicksPerSecond);

        Assert.Equal(20 * TicksPerMs, _connection.RoundTripTime);
        Assert.Equal(20, _connection.Latency);
        Assert.Equal(0, _connection.TimeSyncOffset);
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
        Assert.Equal(-clientAheadTicks, _connection.TimeSyncOffset);

        _clock.Advance(TimeSpan.FromSeconds(10));
        await ExchangeAsync(oneWayTicks: 10 * TicksPerMs, clientAheadTicks);
        Assert.Equal(-clientAheadTicks, _connection.TimeSyncOffset);
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
        long arrived = clientTouchedAt + TicksPerMs;
        _clock.Advance(TimeSpan.FromMilliseconds(18));   // handled a tick after it arrived

        _connection.OnPongReceived(stamp, clientTouchedAt, clientTouchedAt, arrived);

        Assert.Equal(2 * TicksPerMs, _connection.RoundTripTime);
        Assert.Equal(0, _connection.TimeSyncOffset);
    }

    [Fact]
    public void HandOutAnInitialPingRequestOnce()
    {
        // The tick asks every connection every tick, so a request that did not clear would ping on
        // all of them -- once per 16 ms rather than once per ten seconds.
        Assert.False(_connection.TakeInitialTimeSyncPingRequest());

        _connection.RequestInitialTimeSyncPing();

        Assert.True(_connection.TakeInitialTimeSyncPingRequest());
        Assert.False(_connection.TakeInitialTimeSyncPingRequest());
    }

    /// <summary>
    /// The ping is stamped as its send thread writes it, not as the tick queues it (#875): a ping that waited 50 ms for
    /// its send thread carries the time it left, so the round trip holds none of that wait.
    /// </summary>
    [Fact]
    public async Task StampThePing_WhenItIsWritten()
    {
        long queuedAt = _clock.GetUtcNow().UtcTicks;
        _connection.SendTimeSyncPing();
        _clock.Advance(TimeSpan.FromMilliseconds(50));   // the ping waits for its send thread
        long stamp = (await WriteQueuedPingAsync()).ServerTimestamp;
        Assert.Equal(queuedAt + 50 * TicksPerMs, stamp);

        // The client has it a millisecond after it left and answers at once; the pong lands a millisecond later.
        // The round trip is those two milliseconds, not the 50 the ping spent queued.
        _connection.OnPongReceived(stamp, stamp + TicksPerMs, stamp + TicksPerMs, stamp + 2 * TicksPerMs);
        Assert.Equal(2 * TicksPerMs, _connection.RoundTripTime);
    }
}
