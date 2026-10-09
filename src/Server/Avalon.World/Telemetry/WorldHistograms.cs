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

    /// <summary>
    /// The deadline overshoot's buckets: it is signed (an early wake is negative) and mostly within the timer's
    /// precision, so they split early from late around 0 and stay fine below 1 ms before reaching a 1 s stall.
    /// </summary>
    public static readonly double[] OvershootMicroseconds =
        [-1000, -250, -50, 0, 50, 100, 250, 500, 1000, 2000, 4000, 8000, 16667, 33333, 100000, 1000000];

    /// <summary>
    /// The buckets of one stage of a tick (<c>world.post_update.duration</c>): a stage is usually a small part of a
    /// tick, so they start at 25 µs, then follow <see cref="TickMicroseconds" /> to a 1 s stall.
    /// </summary>
    public static readonly double[] StageMicroseconds =
        [25, 50, 100, 250, 500, 1000, 2000, 4000, 8000, 12000, 16667, 25000, 33333, 50000, 100000, 250000, 1000000];

    /// <summary>
    /// A histogram in microseconds with <paramref name="buckets" /> as its buckets, <see cref="TickMicroseconds" />
    /// when none are given.
    /// </summary>
    public static Histogram<double> Microseconds(Meter meter, string name, string description,
        IReadOnlyList<double>? buckets = null) =>
        meter.CreateHistogram<double>(name, "us", description, tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = buckets ?? TickMicroseconds });
}
