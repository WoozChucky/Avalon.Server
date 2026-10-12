using System.Net;
using System.Net.Sockets;
using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.World;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The time-sync exchange on a real <see cref="WorldConnection" /> and its <see cref="ConnectionSender" /> (#892): the
/// ping the tick queues and its send thread stamps and writes, and the pong the tick handles. The stream behind the
/// sender discards what it is given, and the send thread's pass runs on the benchmark's thread, as the scenario runner's
/// does. The clock is the system's, read through a provider that remembers the last time it gave out: the ping's stamp.
/// </summary>
[MemoryDiagnoser]
[MinColumn]
public class TimeSyncBenchmarks
{
    private const int Cycles = 256;
    private const long OneWayTicks = 10 * TimeSpan.TicksPerMillisecond;

    private readonly StampClock _clock = new();
    private NetworkSendScheduler _scheduler = null!;
    private WorldConnection _connection = null!;
    private TcpClient _client = null!;
    private TcpClient _peer = null!;
    private long _outstanding;

    [GlobalSetup]
    public void Setup()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _client = new TcpClient();
        _client.Connect((IPEndPoint)listener.LocalEndpoint);
        _peer = listener.AcceptTcpClient();

        _scheduler = new NetworkSendScheduler(new NetworkConfiguration { SendThreads = 1 }, NullLoggerFactory.Instance,
            _clock, NetworkSendMetrics.Disabled);
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.SendScheduler.Returns(_scheduler);
        _connection = new WorldConnection(server, _client, NullLoggerFactory.Instance, Substitute.For<IPacketReader>(), _clock);
        _connection.InitOutboxForTest(new PacketStream(Stream.Null));

        // One exchange first, so every measured ping carries a round trip, an offset and a client time, as every ping
        // but a connection's first does. Then one ping left unanswered, as between a ping and its pong: what a pong
        // that answers nothing is compared with.
        long stamp = WriteOnePing();
        _connection.OnPongReceived(stamp, stamp + OneWayTicks, stamp + OneWayTicks, stamp + 2 * OneWayTicks);
        _outstanding = WriteOnePing();
    }

    /// <summary>The tick queues a ping and its send thread's pass stamps, encodes, frames and writes it.</summary>
    [Benchmark(OperationsPerInvoke = Cycles)]
    public void PingWritten()
    {
        for (int cycle = 0; cycle < Cycles; cycle++)
            WriteOnePing();
    }

    /// <summary>A ping written, then the pong that answers it handled: one whole exchange.</summary>
    [Benchmark(OperationsPerInvoke = Cycles)]
    public void PingWrittenAndPongHandled()
    {
        for (int cycle = 0; cycle < Cycles; cycle++)
        {
            long stamp = WriteOnePing();
            _connection.OnPongReceived(stamp, stamp + OneWayTicks, stamp + OneWayTicks, stamp + 2 * OneWayTicks);
        }
    }

    /// <summary>A pong whose echo is the stamp of no ping: what a client can send as often as it likes.</summary>
    [Benchmark(OperationsPerInvoke = Cycles)]
    public void PongAnsweringNoPing()
    {
        long echo = _outstanding + 1;
        for (int cycle = 0; cycle < Cycles; cycle++)
            _connection.OnPongReceived(echo, echo + OneWayTicks, echo + OneWayTicks, echo + 2 * OneWayTicks);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _connection.Close();
        _scheduler.RunAllPasses(); // the close's last visit, so the sender finishes and gives back its buffers
        _connection.Dispose();
        _scheduler.Dispose();
        _client.Dispose();
        _peer.Dispose();
    }

    private long WriteOnePing()
    {
        _connection.SendTimeSyncPing();
        _scheduler.RunPass(0);
        return _clock.LastUtcTicks;
    }

    /// <summary>The system clock, remembering the last UTC time it gave out. The sender reads it once per ping.</summary>
    private sealed class StampClock : TimeProvider
    {
        public long LastUtcTicks { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            DateTimeOffset now = base.GetUtcNow();
            LastUtcTicks = now.UtcTicks;
            return now;
        }
    }
}
