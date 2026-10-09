namespace Avalon.LoadTest.Ramp;

/// <summary>Whether a step's post-update stage timings could be read.</summary>
public enum PostUpdateReadout
{
    /// <summary>The world exports the stages and the window held enough samples: <see cref="PostUpdateStages.Stages"/> holds them.</summary>
    Reported,

    /// <summary>The world exports no <c>world.post_update.duration</c> at all: a build from before #875.</summary>
    NotExported,

    /// <summary>A query failed, or the window held too few samples for a rate, as can happen to any of a step's values.</summary>
    Unknown,
}

/// <summary>One stage after the world update over a step's judged window, in microseconds per tick.</summary>
/// <param name="Stage">The <c>stage</c> tag: <c>quests</c>, <c>inventory</c>, ..., <c>outbox</c>, <c>continuations</c>.</param>
/// <param name="MeanUs">The mean time of the stage per tick; null when Prometheus gave none.</param>
/// <param name="P99Us">The 99th percentile, interpolated within the histogram's buckets; null when Prometheus gave none.</param>
public sealed record StageTiming(string Stage, double? MeanUs, double? P99Us);

/// <summary>
/// The world server's <c>world.post_update.duration</c> histogram (#875) over a step's judged window, by stage, in the
/// order the tick runs them. Reported for reading, never a limit: no step is judged on it.
/// </summary>
public sealed record PostUpdateStages(PostUpdateReadout Readout, IReadOnlyList<StageTiming> Stages)
{
    /// <summary>The stages in the order <c>WorldServer.Update</c> runs them; a stage not listed here follows, by name.</summary>
    private static readonly string[] s_tickOrder =
        ["quests", "inventory", "sheet", "ability_amounts", "party_status", "presence", "pings", "outbox", "continuations"];

    /// <summary>Nothing read.</summary>
    public static PostUpdateStages Unknown { get; } = new(PostUpdateReadout.Unknown, []);

    /// <summary>
    /// The stages from three answers: whether the world exports the histogram at all (<paramref name="exportedAnswered"/>
    /// false when that query failed, <paramref name="exportedSeries"/> its series count, null for an empty result), and
    /// the mean and p99 by stage (null when their query failed). A world with no series is
    /// <see cref="PostUpdateReadout.NotExported"/>; a failed query, or no stage with a value, is
    /// <see cref="PostUpdateReadout.Unknown"/>.
    /// </summary>
    public static PostUpdateStages From(bool exportedAnswered, double? exportedSeries,
        IReadOnlyDictionary<string, double>? meanUs, IReadOnlyDictionary<string, double>? p99Us)
    {
        if (!exportedAnswered) return Unknown;
        if (exportedSeries is not > 0) return new(PostUpdateReadout.NotExported, []);
        if (meanUs is null || p99Us is null) return Unknown;

        StageTiming[] stages =
        [
            .. InTickOrder(meanUs.Keys.Union(p99Us.Keys, StringComparer.Ordinal))
                .Select(stage => new StageTiming(stage, Finite(meanUs, stage), Finite(p99Us, stage))),
        ];

        return stages.Any(s => s.MeanUs is not null || s.P99Us is not null)
            ? new(PostUpdateReadout.Reported, stages)
            : Unknown;
    }

    /// <summary>Stage names, each once, in the order the tick runs them; a stage the tick order does not list follows, by name.</summary>
    public static IEnumerable<string> InTickOrder(IEnumerable<string> stages) =>
        stages.Distinct(StringComparer.Ordinal)
            .OrderBy(stage => Array.IndexOf(s_tickOrder, stage) is var at and >= 0 ? at : s_tickOrder.Length)
            .ThenBy(stage => stage, StringComparer.Ordinal);

    private static double? Finite(IReadOnlyDictionary<string, double> values, string stage) =>
        values.TryGetValue(stage, out double value) && double.IsFinite(value) ? value : null;
}
