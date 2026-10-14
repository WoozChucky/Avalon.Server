using System.Diagnostics;
using System.Diagnostics.Metrics;
using Avalon.Hosting.Telemetry;
using Avalon.Network.Packets.Abstractions;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What the tick's metrics cost per packet (#977), with the OpenTelemetry SDK listening as the World server's exporter
/// does (<see cref="Listened" />) and with nothing listening. <see cref="DispatchInput" />: the dispatch telemetry
/// <c>WorldConnection.ProcessQueue</c> wraps every handler run in, for a received input (a high-rate type: no span, no
/// scope; its duration recorded in <c>avalon.packet.handler.duration</c> under its type and outcome), the connection's
/// tags built as ProcessQueue builds them. <see cref="CountSent" />: what <c>WorldConnection.OnQueued</c> adds for every
/// packet the tick sends, <c>network.out.bytes</c> and <c>network.out.packets</c>, untagged. The meter is this
/// benchmark's own, its instruments shaped as production's. The exporter's reader exports nothing within a run.
/// </summary>
[MemoryDiagnoser]
public class PacketMetricsBenchmarks
{
    private const int Packets = 1_000;

    private readonly ActivitySource _source = new("avalon-benchmark-packets");
    private Meter _meter = null!;
    private MeterProvider? _provider;
    private PacketDispatchTelemetry _telemetry = null!;
    private Counter<long> _bytesSent = null!;
    private Counter<long> _packetsSent = null!;
    private Histogram<double> _durationAlone = null!;
    private readonly Guid _connectionId = Guid.NewGuid();

    [Params(false, true)]
    public bool Listened { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _meter = new Meter("avalon-benchmark-packets");
        _telemetry = new PacketDispatchTelemetry(_source, _meter);
        _bytesSent = _meter.CreateCounter<long>("network.out.bytes", "By");
        _packetsSent = _meter.CreateCounter<long>("network.out.packets", "{packets}");
        _durationAlone = _meter.CreateHistogram<double>("benchmark.handler.duration", "ms", "The handler duration's shape, alone",
            tags: null, advice: new InstrumentAdvice<double>
            {
                HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000],
            });
        if (Listened)
        {
            _provider = Sdk.CreateMeterProviderBuilder()
                .AddMeter(_meter.Name)
                .AddReader(new PeriodicExportingMetricReader(new NoExport(), exportIntervalMilliseconds: 3_600_000))
                .Build();
        }
    }

    [Benchmark(OperationsPerInvoke = Packets)]
    public void DispatchInput()
    {
        for (int i = 0; i < Packets; i++)
        {
            PacketDispatch dispatch = _telemetry.Begin(NetworkPacketType.CMSG_PLAYER_INPUT,
                new PacketTags(_connectionId, "127.0.0.1", 42, 7), NullLogger.Instance);
            dispatch.Dispose();
        }
    }

    /// <summary>
    /// The handler duration's record alone, with the two tags the dispatch gives it, with only the type's, and with
    /// none: what each tag costs, for the options in docs/benchmarks.md (#977). Not what production records.
    /// </summary>
    [Benchmark(OperationsPerInvoke = Packets)]
    public void RecordBothTags()
    {
        for (int i = 0; i < Packets; i++)
        {
            _durationAlone.Record(0.004, new KeyValuePair<string, object?>("avalon.packet.type", "CMSG_PLAYER_INPUT"),
                new KeyValuePair<string, object?>("avalon.outcome", "ok"));
        }
    }

    [Benchmark(OperationsPerInvoke = Packets)]
    public void RecordBothTagsSorted()
    {
        for (int i = 0; i < Packets; i++)
        {
            _durationAlone.Record(0.004, new KeyValuePair<string, object?>("avalon.outcome", "ok"),
                new KeyValuePair<string, object?>("avalon.packet.type", "CMSG_PLAYER_INPUT"));
        }
    }

    [Benchmark(OperationsPerInvoke = Packets)]
    public void RecordTypeTag()
    {
        for (int i = 0; i < Packets; i++)
        {
            _durationAlone.Record(0.004, new KeyValuePair<string, object?>("avalon.packet.type", "CMSG_PLAYER_INPUT"));
        }
    }

    [Benchmark(OperationsPerInvoke = Packets)]
    public void RecordUntagged()
    {
        for (int i = 0; i < Packets; i++)
            _durationAlone.Record(0.004);
    }

    [Benchmark(OperationsPerInvoke = Packets)]
    public void CountSent()
    {
        for (int i = 0; i < Packets; i++)
        {
            _bytesSent.Add(64);
            _packetsSent.Add(1);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _provider?.Dispose();
        _meter.Dispose();
        _source.Dispose();
    }

    private sealed class NoExport : BaseExporter<Metric>
    {
        public override ExportResult Export(in Batch<Metric> batch) => ExportResult.Success;
    }
}
