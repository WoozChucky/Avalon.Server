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
    /// wrong world id), or the window held no interval with both counters, as can happen to any of a step's values.
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
    /// Unix seconds on Prometheus's clock); a series's first sample within the window (a new process, or a gap longer
    /// than the samples reach back) is an interval from 0. The world exports both counters together, so a pause interval
    /// is matched with the collections' by process and end time, the collections' increases are summed over the
    /// generations, and the worst interval of any process counts. An interval spanning a missed export averages over the
    /// whole span: a gap can dilute a long collection, never inflate one.
    /// </para>
    /// <para>
    /// A restart is the process's: when any of its counters (the pause time or a generation's collections) is below
    /// the sample before it at an interval's end, the process began again from 0 within the interval, and every one of
    /// its counters counts its whole value there.
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

        Dictionary<(string, long), Added> collected = Increases(collections, windowStart);
        bool measured = false;
        double worst = 0;
        foreach (((string, long) interval, Added paused) in Increases(pause, windowStart))
        {
            if (!collected.TryGetValue(interval, out Added count)) continue;

            measured = true;
            bool restarted = paused.Reset || count.Reset;
            double pauseSeconds = restarted ? paused.Whole : paused.Increase;
            double gcCount = restarted ? count.Whole : count.Increase;
            if (gcCount > 0 && double.IsFinite(pauseSeconds)) worst = Math.Max(worst, pauseSeconds * 1000 / gcCount);
        }

        return measured ? new(GcStallReadout.Reported, worst) : Unknown;
    }

    /// <summary>
    /// Each interval ending within the window, by process and end time (whole milliseconds), summed over the series of
    /// one process: the increase (a series below the sample before it counting its whole value), the whole values at
    /// the interval's end, and whether any series of it was below the sample before it.
    /// </summary>
    private static Dictionary<(string, long), Added> Increases(IReadOnlyList<CounterSeries> series, double windowStart)
    {
        var increases = new Dictionary<(string, long), Added>();
        foreach (CounterSeries counter in series)
        {
            IReadOnlyList<CounterSample> samples = counter.Samples;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].Time <= windowStart) continue;

                // A first sample within the window has nothing before it: it counts from 0.
                double before = i == 0 ? 0 : samples[i - 1].Value;
                bool reset = samples[i].Value < before;
                double increase = reset ? samples[i].Value : samples[i].Value - before;
                (string, long) interval = (counter.Process, (long)Math.Round(samples[i].Time * 1000));
                Added sum = increases.GetValueOrDefault(interval);
                increases[interval] = new Added(sum.Increase + increase, sum.Whole + samples[i].Value, sum.Reset || reset);
            }
        }

        return increases;
    }

    /// <summary>What one process's series of a counter added in one interval.</summary>
    private readonly record struct Added(double Increase, double Whole, bool Reset);
}
