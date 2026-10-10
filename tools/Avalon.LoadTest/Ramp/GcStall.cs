namespace Avalon.LoadTest.Ramp;

/// <summary>Whether a step's worst GC stall could be read.</summary>
public enum GcStallReadout
{
    /// <summary>The world exports its GC pause time and the window held a sample with one before it.</summary>
    Reported,

    /// <summary>The world reports its ticks and no <c>dotnet_gc_pause_time_seconds_total</c> at all: a build without it.</summary>
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
/// The most GC pause time the world added between two of its samples within a step's judged window, in milliseconds:
/// the <c>gc-stall</c> limit's value. The world exports the runtime's total pause time once per export interval (10 s
/// on world 4), so this is the worst interval's pause, read from the raw samples: each sample's increase over the one
/// before it, with no extrapolation.
/// </summary>
/// <param name="Ms">The worst interval's pause; null unless <see cref="Readout"/> is reported.</param>
public sealed record GcStall(GcStallReadout Readout, double? Ms)
{
    /// <summary>Nothing read.</summary>
    public static GcStall Unknown { get; } = new(GcStallReadout.Unknown, null);

    /// <summary>
    /// The worst stall from two answers. Whether the world exports the pause time (<paramref name="exportedAnswered"/>
    /// false when that query failed; <paramref name="exportedSeries"/> its series count, 0 for a world that reports its
    /// ticks and no pause time, null when Prometheus has nothing from the world at all), and the pause time's raw
    /// samples by series (null when that query failed), reaching back before the window so its first sample has one
    /// before it. A count of 0 is <see cref="GcStallReadout.NotExported"/>. A failed or empty count, failed samples, or
    /// no sample within the window (after <paramref name="windowStart"/>, Unix seconds on Prometheus's clock) that has
    /// one before it is <see cref="GcStallReadout.Unknown"/>. A sample below the one before it is a restarted process,
    /// whose count began again at 0: its whole value is what it added.
    /// </summary>
    public static GcStall From(bool exportedAnswered, double? exportedSeries,
        IReadOnlyList<IReadOnlyList<CounterSample>>? series, double windowStart)
    {
        if (!exportedAnswered || exportedSeries is null) return Unknown;
        if (exportedSeries is not > 0) return new(GcStallReadout.NotExported, null);
        if (series is null) return Unknown;

        double? worst = null;
        foreach (IReadOnlyList<CounterSample> samples in series)
        {
            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i].Time <= windowStart) continue;

                double added = samples[i].Value >= samples[i - 1].Value
                    ? samples[i].Value - samples[i - 1].Value
                    : samples[i].Value;
                if (double.IsFinite(added)) worst = Math.Max(worst ?? added, added);
            }
        }

        return worst is { } seconds ? new(GcStallReadout.Reported, seconds * 1000) : Unknown;
    }
}
