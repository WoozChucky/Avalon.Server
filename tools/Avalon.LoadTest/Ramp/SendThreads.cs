namespace Avalon.LoadTest.Ramp;

/// <summary>Whether a step's send-thread values (#875) could be read.</summary>
public enum SendReadout
{
    /// <summary>The world exports its send passes and the window gave a value.</summary>
    Reported,

    /// <summary>
    /// The world reports its ticks and no <c>network.send.pass.duration</c> at all: a build from before the send threads,
    /// which has no slow kick either.
    /// </summary>
    NotExported,

    /// <summary>
    /// Nothing to tell: a query failed, Prometheus has nothing from the world at all (a stalled export, a scrape gap, a
    /// wrong world id), or the window gave no value, as can happen to any of a step's values.
    /// </summary>
    Unknown,
}

/// <summary>
/// The connections the world closed as too slow over a step's judged window (<c>network.out.slow_kicks</c>, #875): the
/// <c>slow-kicks</c> limit's value. The world never drops a packet; a client that cannot keep up is closed instead, so
/// a kick in a ramp is the world, or the bot PC reading its sockets, failing to keep up. Read from the raw samples: each
/// sample's increase over the one before it, with no extrapolation.
/// </summary>
/// <param name="Count">The kicks, every reason together; null unless reported.</param>
public sealed record SlowKicks(SendReadout Readout, double? Count)
{
    /// <summary>Nothing read.</summary>
    public static SlowKicks Unknown { get; } = new(SendReadout.Unknown, null);

    /// <summary>
    /// The kicks from two answers. Whether the world exports its send passes (<paramref name="exportedAnswered"/> false
    /// when that query failed; <paramref name="exportedSeries"/> the pass histogram's series count, 0 for a world that
    /// reports its ticks and no send pass, null when Prometheus has nothing from the world at all), and the raw samples
    /// of every reason's kick counter (null when their query failed), reaching back before the window so its first
    /// sample has one before it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The kicks are the increases of the samples within the window (after <paramref name="windowStart"/>, Unix seconds
    /// on Prometheus's clock), each over the sample before it, summed over every series. A reason's series exists only
    /// from its first kick, so a series' first sample within the window (a reason's first kick, or a new process) counts
    /// from 0: its whole value. A sample below the one before it is a restarted process counting again from 0, and
    /// counts its whole value.
    /// </para>
    /// <para>
    /// A count of 0 is <see cref="SendReadout.NotExported"/>: the step is not judged on slow kicks. A failed or empty
    /// count, or failed samples, is <see cref="SendReadout.Unknown"/>. A world that exports its send passes and has no
    /// kick series has kicked no one: 0, judged.
    /// </para>
    /// </remarks>
    public static SlowKicks From(bool exportedAnswered, double? exportedSeries, IReadOnlyList<CounterSeries>? kicks,
        double windowStart)
    {
        if (!exportedAnswered || exportedSeries is null) return Unknown;
        if (exportedSeries is not > 0) return new(SendReadout.NotExported, null);
        if (kicks is null) return Unknown;

        double count = 0;
        foreach (CounterSeries series in kicks)
        {
            IReadOnlyList<CounterSample> samples = series.Samples;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].Time <= windowStart) continue;

                double before = i == 0 ? 0 : samples[i - 1].Value;
                count += samples[i].Value < before ? samples[i].Value : samples[i].Value - before;
            }
        }

        return new(SendReadout.Reported, count);
    }
}

/// <summary>
/// The send threads over a step's judged window (#875), for the report only: no limit reads them. Their busy time is
/// the share of the window they spent in passes (<c>network.send.pass.duration</c>'s sum rate, tagged <c>thread</c>);
/// the pending bytes are, per pass, the most any one connection it visited had queued or being written
/// (<c>network.out.pending_bytes</c>).
/// </summary>
/// <param name="Threads">The send threads that ran a pass in the window; null unless their busy time was read.</param>
/// <param name="BusyCores">All the threads' busy time together, in cores; null unless read.</param>
/// <param name="BusiestThread">The busiest thread's busy time, a fraction of one core; null unless read.</param>
/// <param name="PendingBytesP99">The 99th percentile of a pass's largest pending bytes; null unless read.</param>
public sealed record SendThreads(SendReadout Readout, int? Threads, double? BusyCores, double? BusiestThread,
    double? PendingBytesP99)
{
    /// <summary>Nothing read.</summary>
    public static SendThreads Unknown { get; } = new(SendReadout.Unknown, null, null, null, null);

    /// <summary>
    /// The send threads from three answers. Whether the world exports its send passes (as for
    /// <see cref="SlowKicks.From"/>), each thread's busy time in cores (null when that query failed) and the pending
    /// bytes' p99 (null when its query failed or gave nothing). A count of 0 is <see cref="SendReadout.NotExported"/>. A
    /// failed or empty count is <see cref="SendReadout.Unknown"/>, as are no thread with a finite busy time and no p99
    /// together; one missing leaves its values null and the other's reported.
    /// </summary>
    public static SendThreads From(bool exportedAnswered, double? exportedSeries,
        IReadOnlyDictionary<string, double>? busyByThread, double? pendingP99)
    {
        if (!exportedAnswered || exportedSeries is null) return Unknown;
        if (exportedSeries is not > 0) return new(SendReadout.NotExported, null, null, null, null);

        double[] busy = busyByThread is null ? [] : [.. busyByThread.Values.Where(double.IsFinite)];
        double? pending = pendingP99 is { } p && double.IsFinite(p) ? p : null;
        if (busy.Length == 0 && pending is null) return Unknown;

        return busy.Length == 0
            ? new(SendReadout.Reported, null, null, null, pending)
            : new(SendReadout.Reported, busy.Length, busy.Sum(), busy.Max(), pending);
    }
}
