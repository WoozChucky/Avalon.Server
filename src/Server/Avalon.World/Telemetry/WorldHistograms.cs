using System.Diagnostics.Metrics;

namespace Avalon.World.Telemetry;

/// <summary>
/// The tick-time histograms' buckets, in microseconds. The SDK's default buckets end at 10 ms, so every slower tick
/// fell into +Inf and a p95 or p99 could never read above 10 ms; these reach a 1 s stall, with one frame at 60 Hz
/// (16667 µs) as an edge so "over one frame" is exact.
/// </summary>
public static class WorldHistograms
{
    public static readonly double[] TickMicroseconds =
        [250, 500, 1000, 2000, 4000, 8000, 12000, 16667, 25000, 33333, 50000, 100000, 250000, 1000000];

    /// <summary>A histogram in microseconds with <see cref="TickMicroseconds" /> as its buckets.</summary>
    public static Histogram<double> Microseconds(Meter meter, string name, string description) =>
        meter.CreateHistogram<double>(name, "us", description, tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = TickMicroseconds });
}
