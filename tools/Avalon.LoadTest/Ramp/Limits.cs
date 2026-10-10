using System.Globalization;

namespace Avalon.LoadTest.Ramp;

/// <summary>A value a ramp step is judged on.</summary>
public enum LimitName { TickP99, Tps, AckP95, SlowKicks, Admission, Memory, GcStall, GcPause, SaveP95, GenCpu, GenLag }

/// <summary>
/// One limit: a step breaches it when its value is above <see cref="Threshold"/> (<see cref="TripsAbove"/>), or below it.
/// </summary>
/// <param name="CliName">The name <c>--limit name=value</c> takes.</param>
/// <param name="Unit">How the value reads: <c>ms</c>, <c>fraction</c>, <c>ticks/s</c> or <c>count</c>.</param>
public sealed record Limit(LimitName Name, string CliName, double Threshold, bool TripsAbove, string Unit)
{
    /// <summary>Whether <paramref name="value"/> is on the tripping side of the threshold.</summary>
    public bool IsBreachedBy(double value) => TripsAbove ? value > Threshold : value < Threshold;
}

/// <summary>The ramp's limits: the defaults, and those with the command line's overrides applied.</summary>
public static class Limits
{
    private const string FractionUnit = "fraction";

    /// <summary>The default limits, in the order the report lists them.</summary>
    public static IReadOnlyList<Limit> Defaults { get; } =
    [
        new(LimitName.TickP99, "tick-p99", 16.7, TripsAbove: true, "ms"),
        new(LimitName.Tps, "tps", 58, TripsAbove: false, "ticks/s"),
        new(LimitName.AckP95, "ack-p95", 150, TripsAbove: true, "ms"),
        // The connections the world closed as too slow (#875): it drops no packet, it closes a client that cannot keep up.
        new(LimitName.SlowKicks, "slow-kicks", 0, TripsAbove: true, "count"),
        new(LimitName.Admission, "admission", 0.01, TripsAbove: true, FractionUnit),
        new(LimitName.Memory, "memory", 0.85, TripsAbove: true, FractionUnit),
        // The worst average pause per collection in one of the world's sample intervals: above one tick. A gen2 count
        // would also count background collections, which barely pause the process, and a sum would count a few ordinary
        // collections as one stall.
        new(LimitName.GcStall, "gc-stall", 16.7, TripsAbove: true, "ms"),
        new(LimitName.GcPause, "gc-pause", 0.05, TripsAbove: true, FractionUnit),
        new(LimitName.SaveP95, "save-p95", 1000, TripsAbove: true, "ms"),
        new(LimitName.GenCpu, "gen-cpu", 0.80, TripsAbove: true, FractionUnit),
        new(LimitName.GenLag, "gen-lag", 5, TripsAbove: true, "ms"),
    ];

    /// <summary>
    /// The defaults with each <c>name=value</c> override applied; the value is in the default's unit (<c>tick-p99=20</c>
    /// is 20 ms, <c>memory=0.9</c> is 90 %).
    /// </summary>
    /// <exception cref="CommandLineException">
    /// An override is refused when it is not <c>name=value</c>, its name is not a limit's, its value is not a finite
    /// number, its value is negative, or a fraction limit's value is outside 0 to 1.
    /// </exception>
    public static IReadOnlyList<Limit> WithOverrides(IEnumerable<string> overrides)
    {
        Limit[] limits = [.. Defaults];
        foreach (string entry in overrides)
        {
            int equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
                throw new CommandLineException($"--limit {entry}: expected name=value.");

            string name = entry[..equals].Trim();
            int index = Array.FindIndex(limits, l => string.Equals(l.CliName, name, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new CommandLineException(
                    $"--limit {entry}: unknown limit '{name}'; known: {string.Join(", ", Defaults.Select(l => l.CliName))}.");
            }

            if (!double.TryParse(entry[(equals + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold)
                || !double.IsFinite(threshold))
            {
                throw new CommandLineException($"--limit {entry}: '{entry[(equals + 1)..]}' is not a finite number.");
            }

            if (threshold < 0)
            {
                throw new CommandLineException($"--limit {entry}: {name} cannot be negative.");
            }

            if (limits[index].Unit == FractionUnit && threshold > 1)
            {
                throw new CommandLineException($"--limit {entry}: {name} is a fraction, e.g. 0.85.");
            }

            limits[index] = limits[index] with { Threshold = threshold };
        }

        return limits;
    }

    /// <summary>Whether the limit measures the bot PC rather than the server.</summary>
    public static bool IsGenerator(LimitName name) => name is LimitName.GenCpu or LimitName.GenLag;
}
