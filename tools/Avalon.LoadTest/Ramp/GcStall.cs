namespace Avalon.LoadTest.Ramp;

/// <summary>Whether a step's GC stall could be read.</summary>
public enum GcStallReadout
{
    /// <summary>The world exports its GC pause time and collections, and the window held an interval of both.</summary>
    Reported,

    /// <summary>
    /// The world reports its ticks and not both <c>dotnet_gc_pause_time_seconds_total</c> and
    /// <c>dotnet_gc_collections_total</c>: a build without them.
    /// </summary>
    NotExported,

    /// <summary>
    /// Nothing to tell: a query failed, Prometheus has nothing from the world at all (a stalled export, a scrape gap, a
    /// wrong world id), or the window held no sample with one before it, as can happen to any of a step's values.
    /// </summary>
    Unknown,
}

/// <summary>One sample of a counter series as Prometheus stores it: its time (Unix seconds) and its value.</summary>
public readonly record struct CounterSample(double Time, double Value);

/// <summary>
/// A counter series's raw samples, oldest first, and the process they came from: the series's labels other than its
/// name and any label summed over (the collections' <c>gc_heap_generation</c>), so a pause series and the collection
/// series of the same process share it.
/// </summary>
public sealed record CounterSeries(string Process, IReadOnlyList<CounterSample> Samples);

/// <summary>
/// The worst average GC pause per collection over a step's judged window, in milliseconds: the <c>gc-stall</c> limit's
/// value. The world exports the runtime's total pause time and its collections once per export interval (10 s on world
/// 4); for each interval in the window, the pause it added is divided by the collections it added, and the worst
/// interval counts. A sum per interval would trip on a few ordinary collections; the average flags a long one. Read
/// from the raw samples: each sample's increase over the one before it, with no extrapolation.
/// </summary>
/// <param name="Ms">The worst interval's average; 0 when no interval in the window had a collection; null unless reported.</param>
public sealed record GcStall(GcStallReadout Readout, double? Ms)
{
    /// <summary>Nothing read.</summary>
    public static GcStall Unknown { get; } = new(GcStallReadout.Unknown, null);

    /// <summary>
    /// The worst average from three answers. Whether the world exports the pause time and the collections
    /// (<paramref name="exportedAnswered"/> false when that query failed; <paramref name="exportedSeries"/> the pause
    /// series count when both are there, 0 for a world that reports its ticks and not both, null when Prometheus has
    /// nothing from the world at all), and the raw samples of the pause time and of the collections, every generation's
    /// series (null when their query failed), reaching back before the window so its first sample has one before it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An interval is one sample and the one before it, ending within the window (after <paramref name="windowStart"/>,
    /// Unix seconds on Prometheus's clock); the world exports both counters together, so a pause interval is matched
    /// with the collections' by process and end time, and the collections' increases are summed over the generations.
    /// A sample below the one before it is a restarted process, whose count began again at 0: its whole value is what
    /// it added.
    /// </para>
    /// <para>
    /// A count of 0 is <see cref="GcStallReadout.NotExported"/>. A failed or empty count, failed samples, or no interval
    /// in the window with both counters is <see cref="GcStallReadout.Unknown"/>. An interval without a collection has
    /// no average; a window whose intervals had none reads 0, judged.
    /// </para>
    /// </remarks>
    public static GcStall From(bool exportedAnswered, double? exportedSeries, IReadOnlyList<CounterSeries>? pause,
        IReadOnlyList<CounterSeries>? collections, double windowStart)
    {
        if (!exportedAnswered || exportedSeries is null) return Unknown;
        if (exportedSeries is not > 0) return new(GcStallReadout.NotExported, null);
        if (pause is null || collections is null) return Unknown;

        Dictionary<(string, long), double> collected = Increases(collections, windowStart);
        bool measured = false;
        double worst = 0;
        foreach (((string, long) interval, double paused) in Increases(pause, windowStart))
        {
            if (!collected.TryGetValue(interval, out double count)) continue;

            measured = true;
            if (count > 0 && double.IsFinite(paused)) worst = Math.Max(worst, paused * 1000 / count);
        }

        return measured ? new(GcStallReadout.Reported, worst) : Unknown;
    }

    /// <summary>
    /// Each interval's increase ending within the window, by process and end time (whole milliseconds), summed over the
    /// series of one process.
    /// </summary>
    private static Dictionary<(string, long), double> Increases(IReadOnlyList<CounterSeries> series, double windowStart)
    {
        var increases = new Dictionary<(string, long), double>();
        foreach (CounterSeries counter in series)
        {
            IReadOnlyList<CounterSample> samples = counter.Samples;
            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i].Time <= windowStart) continue;

                double added = samples[i].Value >= samples[i - 1].Value
                    ? samples[i].Value - samples[i - 1].Value
                    : samples[i].Value;
                (string, long) interval = (counter.Process, (long)Math.Round(samples[i].Time * 1000));
                increases[interval] = increases.GetValueOrDefault(interval) + added;
            }
        }

        return increases;
    }
}
