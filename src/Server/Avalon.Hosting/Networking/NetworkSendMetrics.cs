using System.Diagnostics.Metrics;

namespace Avalon.Hosting.Networking;

/// <summary>The send threads' metrics (#875), on the meter the host hands in (the world's: <c>world-server</c>).</summary>
public sealed class NetworkSendMetrics
{
    private static readonly double[] s_passMicroseconds = [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 16667, 50000, 250000];
    private static readonly long[] s_pendingBytes = [1024, 4096, 16384, 65536, 131072, 262144, 524288, 1048576];
    private static readonly int[] s_burstPackets = [1, 2, 4, 8, 16, 32, 64, 128, 256];
    private static readonly KeyValuePair<string, object?> s_bytesReason = new("reason", "bytes");
    private static readonly KeyValuePair<string, object?> s_stallReason = new("reason", "stall");

    private readonly Histogram<double> _passDuration;
    private readonly Histogram<long> _pendingBytes;
    private readonly Counter<long> _bytes;
    private readonly Histogram<int> _burstPackets;
    private readonly Counter<long> _threadFaults;
    private readonly Counter<long> _slowKicks;

    public NetworkSendMetrics(Meter meter)
    {
        _passDuration = meter.CreateHistogram<double>("network.send.pass.duration", "us",
            "One send thread's pass over the connections it was woken for, by thread", tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = s_passMicroseconds });
        _pendingBytes = meter.CreateHistogram<long>("network.out.pending_bytes", "By",
            "Per send pass, the most bytes any one connection it visited had queued or being written", tags: null,
            advice: new InstrumentAdvice<long> { HistogramBucketBoundaries = s_pendingBytes });
        _bytes = meter.CreateCounter<long>("network.send.bytes", "By", "Bytes the send threads handed to the sockets");
        _burstPackets = meter.CreateHistogram<int>("network.send.burst_packets", "{packets}", "Packets per write",
            tags: null, advice: new InstrumentAdvice<int> { HistogramBucketBoundaries = s_burstPackets });
        _threadFaults = meter.CreateCounter<long>("network.send.thread_faults", "{faults}",
            "Failures that escaped a send pass, by thread; the thread goes on");
        _slowKicks = meter.CreateCounter<long>("network.out.slow_kicks", "{connections}",
            "Connections closed as too slow, by reason: bytes (Network:MaxPendingBytes) or stall (Network:MaxWriteStall)");
    }

    /// <summary>Records into a meter nothing listens to.</summary>
    public static NetworkSendMetrics Disabled { get; } = new(new Meter("avalon-network-disabled"));

    internal void Pass(KeyValuePair<string, object?> thread, TimeSpan duration, long maxPendingBytes)
    {
        _passDuration.Record(duration.TotalMicroseconds, thread);
        _pendingBytes.Record(maxPendingBytes);
    }

    internal void Burst(int packets, int bytes)
    {
        _burstPackets.Record(packets);
        _bytes.Add(bytes);
    }

    internal void ThreadFault(KeyValuePair<string, object?> thread) => _threadFaults.Add(1, thread);

    internal void SlowKick(SlowKickReason reason) =>
        _slowKicks.Add(1, reason == SlowKickReason.Bytes ? s_bytesReason : s_stallReason);
}
