using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World.Testing.Fakes;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The time-sync exchange on an admitted <c>WorldConnection</c> with its real sender and pong handler (#892): the ping
/// the tick queues and its send thread stamps and writes, the pong as the read loop delivers it, and the tick's session
/// pass that handles it. The stream behind the sender discards what it is given, and the send thread's pass runs on the
/// benchmark's thread, as the scenario runner's does. The clock is the fixture's: a ping is written at one fixed time
/// and its pong arrives 20 ms later, so every exchange measures the same round trip.
/// </summary>
[MemoryDiagnoser]
[MinColumn]
public class TimeSyncBenchmarks
{
    private const int Cycles = 256;
    private const int SurplusPerPass = 32;
    private const long OneWayTicks = 10 * TimeSpan.TicksPerMillisecond;

    private readonly CPongPacket _pong = new();
    private ReceiveQueueFixture _fixture = null!;
    private DateTimeOffset _writtenAt;
    private DateTimeOffset _arrivesAt;
    private long _stamp;

    [GlobalSetup]
    public void Setup()
    {
        _fixture = new ReceiveQueueFixture(Stream.Null);
        _writtenAt = _fixture.Clock.Now;
        _arrivesAt = _writtenAt + TimeSpan.FromTicks(2 * OneWayTicks);
        _stamp = _writtenAt.UtcTicks;

        // One exchange first, so every measured ping carries a round trip, an offset and a client time, as every ping
        // but a connection's first does. It also proves the pong below is one the server accepts: without it, a change
        // that made the handler ignore this pong would leave the benchmark timing the ignored path.
        WriteOnePing();
        Arrive(_stamp);
        _fixture.Pump();
        if (_fixture.Connection.RoundTripTime != 2 * OneWayTicks)
        {
            throw new InvalidOperationException(
                $"The benchmark's pong was not accepted: round trip {_fixture.Connection.RoundTripTime} ticks, expected {2 * OneWayTicks}");
        }
    }

    /// <summary>The tick queues a ping and its send thread's pass stamps, encodes, frames and writes it.</summary>
    [Benchmark(OperationsPerInvoke = Cycles)]
    public void PingWritten()
    {
        for (int cycle = 0; cycle < Cycles; cycle++)
            WriteOnePing();
    }

    /// <summary>A ping written, then the pong that answers it arrives and the session pass handles it: one exchange.</summary>
    [Benchmark(OperationsPerInvoke = Cycles)]
    public void PingWrittenAndPongHandled()
    {
        for (int cycle = 0; cycle < Cycles; cycle++)
        {
            WriteOnePing();
            Arrive(_stamp);
            _fixture.Pump();
        }
    }

    /// <summary>
    /// A ping written, then a pong that echoes the stamp of no ping: it is the ping's one answer, reaches the handler and
    /// is ignored there.
    /// </summary>
    [Benchmark(OperationsPerInvoke = Cycles)]
    public void PingWrittenAndWrongPongHandled()
    {
        for (int cycle = 0; cycle < Cycles; cycle++)
        {
            WriteOnePing();
            Arrive(_stamp + 1);
            _fixture.Pump();
        }
    }

    /// <summary>
    /// A pong beyond one per ping, 32 of them between two session passes: what a client can send at any rate. Since #892
    /// it is dropped as it arrives; before, it was queued and handled like any other.
    /// </summary>
    [Benchmark(OperationsPerInvoke = Cycles * SurplusPerPass)]
    public void SurplusPong()
    {
        for (int cycle = 0; cycle < Cycles; cycle++)
        {
            for (int pong = 0; pong < SurplusPerPass; pong++)
                Arrive(_stamp);
            _fixture.Pump();
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();

    private void WriteOnePing()
    {
        _fixture.Connection.SendTimeSyncPing();
        _fixture.Scheduler!.RunPass(0);
    }

    /// <summary>The read loop delivers a pong 20 ms after the ping was written; the clock then goes back for the next ping.</summary>
    private void Arrive(long echo)
    {
        _pong.LastServerTimestamp = echo;
        _pong.ClientReceivedTimestamp = _stamp + OneWayTicks;
        _pong.ClientSentTimestamp = _stamp + OneWayTicks;
        _fixture.Clock.Now = _arrivesAt;
        _fixture.Connection.Deliver(NetworkPacketType.CMSG_PONG, _pong);
        _fixture.Clock.Now = _writtenAt;
    }
}
