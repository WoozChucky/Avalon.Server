using System.Diagnostics;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// Runs a scenario and measures it: a wall-clock warm-up, then <see cref="Windows" /> windows of
/// <see cref="WindowTicks" /> ticks whose least tick-thread allocation is the gated figure, checked afterwards by
/// <see cref="IScenario.Verify" /> to have done the scenario's work, then an optional timing and GC phase. A scenario
/// of a fixed length (<see cref="IScenario.Length" />) is measured over its fixed ticks instead. The loops only record,
/// into arrays allocated before them, so the measurement adds nothing to what it measures; sorting, percentiles and the
/// report come after.
/// </summary>
public static class ScenarioMeasurement
{
    /// <summary>One second of ticks at 60 Hz.</summary>
    public const int WindowTicks = 60;

    /// <summary>How many windows are measured; the least is kept, so one disturbed window does not move the figure.</summary>
    public const int Windows = 5;

    private const double TickBudgetMs = 1000d / 60d;

    /// <summary>
    /// Warms up for <paramref name="warmup" /> of wall-clock time, measures <see cref="Windows" /> windows
    /// (<see cref="ScenarioReport.BytesPerWindow" /> is the least), then, when <paramref name="measureTicks" /> is
    /// above zero, times that many more ticks and counts their allocations and collections.
    /// </summary>
    /// <remarks>
    /// A scenario of a fixed length (<see cref="IScenario.Length" />) is a fight, never in a steady state, so a
    /// wall-clock warm-up would end at a different point of it on every machine. It is run twice instead. First a
    /// rehearsal on a world of its own, for at least the fixed length and at least <paramref name="warmup" />, so the
    /// JIT's tiering, first-use caches and pools settle on the same code the measured run takes. Then a fresh world:
    /// its fixed warm-up ticks, then its measured ticks, every one of them counted. Its
    /// <see cref="ScenarioReport.BytesPerWindow" /> is their mean per window (every kill, death and drop of the run is
    /// in it, where the least window would keep only the quietest second), and, when <paramref name="measureTicks" />
    /// is above zero, its timing and collections are those of the same measured ticks: their number is the scenario's,
    /// not <paramref name="measureTicks" />.
    /// </remarks>
    public static ScenarioReport Run(IScenario scenario, TimeSpan warmup, int measureTicks)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentOutOfRangeException.ThrowIfNegative(measureTicks);

        if (scenario.Length is { } length)
            return RunFixed(scenario, length, warmup, measureTicks > 0);

        using ScenarioWorld world = scenario.Build();

        long[] windowBytes = new long[Windows];
        long[] tickBytes = new long[measureTicks];
        long[] tickDurations = new long[measureTicks];

        WarmUp(world, warmup);

        // Before the collection, so what the mark allocates is long gone from the windows' point of view.
        world.MarkProgress();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        MeasureWindows(world, windowBytes);

        // Outside the measured region: a scenario that stopped walking or fighting would read as cheaper.
        scenario.Verify(world);

        TimingPhase timing = measureTicks > 0 ? MeasureTicks(world, tickBytes, tickDurations) : default;

        return Report(scenario, world, windowBytes.Min(), tickBytes, tickDurations, timing);
    }

    private static ScenarioReport RunFixed(IScenario scenario, FixedLength length, TimeSpan warmup, bool timed)
    {
        Rehearse(scenario, length, warmup);

        using ScenarioWorld world = scenario.Build();

        long[] tickBytes = new long[length.MeasuredTicks];
        long[] tickDurations = new long[length.MeasuredTicks];

        for (int t = 0; t < length.WarmupTicks; t++)
            world.Tick();

        world.MarkProgress();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        TimingPhase timing = MeasureTicks(world, tickBytes, tickDurations);

        // Outside the measured region, as for every scenario: a fight that stopped would read as cheaper.
        scenario.Verify(world);

        long bytesPerWindow = tickBytes.Sum() / length.Windows;
        return timed
            ? Report(scenario, world, bytesPerWindow, tickBytes, tickDurations, timing)
            : Report(scenario, world, bytesPerWindow, [], [], default);
    }

    /// <summary>
    /// A fixed-length scenario's rehearsal: a world of its own ticked for the scenario's whole length, and on until
    /// <paramref name="warmup" /> has passed, then disposed.
    /// </summary>
    private static void Rehearse(IScenario scenario, FixedLength length, TimeSpan warmup)
    {
        using ScenarioWorld rehearsal = scenario.Build();
        int ticks = length.WarmupTicks + length.MeasuredTicks;
        long start = Stopwatch.GetTimestamp();
        for (int t = 0; t < ticks || Stopwatch.GetElapsedTime(start) < warmup; t++)
            rehearsal.Tick();
    }

    private static void WarmUp(ScenarioWorld world, TimeSpan warmup)
    {
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < warmup)
            world.Tick();
    }

    private static void MeasureWindows(ScenarioWorld world, long[] windowBytes)
    {
        for (int w = 0; w < windowBytes.Length; w++)
        {
            long total = 0;
            for (int t = 0; t < WindowTicks; t++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                world.Tick();
                total += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            windowBytes[w] = total;
        }
    }

    private static TimingPhase MeasureTicks(ScenarioWorld world, long[] tickBytes, long[] tickDurations)
    {
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        TimeSpan pause = GC.GetTotalPauseDuration();
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();

        for (int t = 0; t < tickDurations.Length; t++)
        {
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            long tickStart = Stopwatch.GetTimestamp();
            world.Tick();
            tickDurations[t] = Stopwatch.GetTimestamp() - tickStart;
            tickBytes[t] = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
        }

        TimeSpan wall = Stopwatch.GetElapsedTime(start);
        return new TimingPhase(
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2,
            GC.GetTotalPauseDuration() - pause,
            GC.GetTotalAllocatedBytes(precise: true) - allocated,
            wall);
    }

    private static ScenarioReport Report(IScenario scenario, ScenarioWorld world, long bytesPerWindow,
        long[] tickBytes, long[] tickDurations, TimingPhase timing)
    {
        int players = scenario.Players;
        double bytesPerPlayerPerTick = players > 0 ? bytesPerWindow / (double)WindowTicks / players : 0;

        long[] bytes = Sorted(tickBytes);
        double[] ms = Array.ConvertAll(Sorted(tickDurations), d => d * 1000d / Stopwatch.Frequency);
        int overBudget = Array.FindAll(ms, d => d > TickBudgetMs).Length;
        double pauseMs = timing.Pause.TotalMilliseconds;
        double wallMs = timing.Wall.TotalMilliseconds;

        return new ScenarioReport(
            scenario.Name, players, world.Instances,
            bytesPerWindow, bytesPerPlayerPerTick,
            bytes.Length > 0 ? bytes.Average() : 0, Percentile(bytes, 0.95), bytes.Length > 0 ? bytes[^1] : 0,
            timing.TotalAllocated,
            ms.Length > 0 ? ms.Average() : 0, Percentile(ms, 0.50), Percentile(ms, 0.95), Percentile(ms, 0.99),
            ms.Length > 0 ? ms[^1] : 0, ms.Length > 0 ? overBudget * 100d / ms.Length : 0,
            timing.Gen0, timing.Gen1, timing.Gen2, pauseMs, wallMs > 0 ? pauseMs * 100d / wallMs : 0,
            RuntimeStamp.Current());
    }

    private static T[] Sorted<T>(T[] values)
    {
        var copy = (T[])values.Clone();
        Array.Sort(copy);
        return copy;
    }

    /// <summary>The nearest-rank percentile of sorted values; zero when there are none.</summary>
    private static T Percentile<T>(T[] sorted, double p) where T : struct =>
        sorted.Length == 0
            ? default
            : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];

    /// <summary>What the timing phase counted; all zero when the run had none.</summary>
    private readonly record struct TimingPhase(
        int Gen0, int Gen1, int Gen2, TimeSpan Pause, long TotalAllocated, TimeSpan Wall);
}
