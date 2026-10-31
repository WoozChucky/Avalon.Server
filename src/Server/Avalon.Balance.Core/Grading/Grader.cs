using CreatureRarity = Avalon.World.Public.Enums.CreatureRarity;

namespace Avalon.Balance.Core;

public sealed record GradedMetric(RowKey? Row, string Check, string Metric, double? Value, Band Band, string Unit, Grade Grade)
{
    /// <summary>How far out, relative to the edge crossed; missing values count as the farthest.</summary>
    public double Severity => Value is not { } v ? double.MaxValue
        : Band.Distance(v) / Math.Max(1d, Math.Abs(Band.Min is { } lo && v < lo ? lo : Band.Max ?? 1d));
}

public sealed class GradeReport(IReadOnlyList<GradedMetric> metrics)
{
    public IReadOnlyList<GradedMetric> Metrics { get; } = metrics;

    public bool AnyRed => Metrics.Any(m => m.Grade == Grade.Red);

    public int Count(Grade grade) => Metrics.Count(m => m.Grade == grade);

    public IEnumerable<GradedMetric> Worst(int n) =>
        Metrics.Where(m => m.Grade != Grade.Green).OrderByDescending(m => m.Grade).ThenByDescending(m => m.Severity).Take(n);

    public GradedMetric? For(RowKey row, string metric) =>
        Metrics.FirstOrDefault(m => m.Row == row && string.Equals(m.Metric, metric, StringComparison.Ordinal));
}

public static class Grader
{
    /// <summary>
    /// Grades each row of <see cref="TargetFile.GradedGear" /> against its scenario's bands, then the global checks
    /// (flat curve and resource flow per build, class parity per class by its best build) over those rows only, and the
    /// levelling pace from the data alone.
    /// </summary>
    /// <remarks>The targets must have passed <see cref="TargetFile.Validate" />, which checks yellowTolerancePct.</remarks>
    public static GradeReport Grade(IReadOnlyList<RowResult> rows, BalanceData data, ScenarioFile scenarios, TargetFile targets)
    {
        double tol = targets.YellowTolerancePct;
        GlobalTargets g = targets.Global;
        var graded = rows.Where(r => string.Equals(r.Key.Gear, targets.GradedGear, StringComparison.Ordinal)).ToList();
        var metrics = new List<GradedMetric>();

        void Add(RowKey? row, string check, string metric, double? value, Band band, string unit) =>
            metrics.Add(new GradedMetric(row, check, metric, value, band, unit, band.Grade(value, tol)));

        foreach (RowResult r in graded)
        {
            if (!targets.Scenarios.TryGetValue(r.Key.Scenario, out ScenarioTargets? t)) continue;
            if (t.WinRate is { } win) Add(r.Key, "scenario", "win rate", r.WinRatePct, win, " %");
            if (t.FightSeconds is { } fight) Add(r.Key, "scenario", "fight length", r.FightSeconds.Median, fight, " s");
            if (t.HealthLeftPct is { } hp) Add(r.Key, "scenario", "health left", r.HealthLeftPct?.Median, hp, " %");
        }

        // Flat curve: same-level scenarios, per build, across levels.
        foreach (IGrouping<(string, string), RowResult> curve in graded
                     .Where(r => scenarios.Scenarios.Any(s => s.Id == r.Key.Scenario && s.SameLevel))
                     .GroupBy(r => (r.Key.Build, r.Key.Scenario)))
        {
            if (curve.Count() < 2) continue;
            string name = $"{curve.Key.Item1} {curve.Key.Item2}";
            Add(null, "flat curve", $"{name} win rate spread",
                curve.Max(r => r.WinRatePct) - curve.Min(r => r.WinRatePct), new Band { Min = 0, Max = g.FlatCurveWinRatePoints }, " pts");
            double shortest = curve.Min(r => r.FightSeconds.Median);
            Add(null, "flat curve", $"{name} fight length spread",
                shortest > 0 ? (curve.Max(r => r.FightSeconds.Median) - shortest) * 100d / shortest : null,
                new Band { Min = 0, Max = g.FlatCurveFightLengthPct }, " %");
        }

        // Class parity, per level: each class is scored by its best build against the average of the classes' best.
        // The best is the highest win rate; on a tie above 0 % the shorter median fight (it wins sooner), at 0 % the
        // longer (it survives longer); then the build named first in scenarios.json. The metric is recorded on the
        // best build's row; a class's other builds get none and do not move the average.
        foreach (IGrouping<ushort, RowResult> level in graded.Where(r => r.Key.Scenario == g.ParityScenario).GroupBy(r => r.Key.Level))
        {
            RowResult[] best =
            [
                .. level.GroupBy(r => r.Key.Class).Select(c => c
                    .OrderByDescending(r => r.WinRatePct)
                    .ThenBy(r => r.WinRatePct > 0 ? r.FightSeconds.Median : -r.FightSeconds.Median)
                    .ThenBy(r => BuildOrder(scenarios, r.Key.Build))
                    .First()),
            ];
            double avgWin = best.Average(r => r.WinRatePct);
            double avgFight = best.Average(r => r.FightSeconds.Median);
            foreach (RowResult r in best)
            {
                Add(r.Key, "class parity", $"{r.Key.Class} L{r.Key.Level} win rate vs average", r.WinRatePct - avgWin,
                    new Band { Min = -g.ParityWinRatePoints, Max = g.ParityWinRatePoints }, " pts");
                Add(r.Key, "class parity", $"{r.Key.Class} L{r.Key.Level} fight length vs average",
                    avgFight > 0 ? (r.FightSeconds.Median - avgFight) * 100d / avgFight : null,
                    new Band { Min = -g.ParityFightLengthPct, Max = g.ParityFightLengthPct }, " %");
            }
        }

        // Resource flow. #1181: every class spends mana and enters full, so every class is graded on how long it
        // starves, and nothing on how soon it first spends.
        foreach (RowResult r in graded.Where(r => r.Key.Scenario == g.ResourceScenario))
        {
            Add(r.Key, "resource flow", $"{r.Key.Build} L{r.Key.Level} starved share", r.StarvedPct.Median,
                new Band { Min = 0, Max = g.CasterStarvedPct }, " %");
        }

        // Levelling pace: same-level normal kills to the next level, experience as CreatureStatDeriver derives it.
        foreach (ushort level in scenarios.LevelRange())
        {
            if (data.RequiredExperience(level) is not { } needed) continue;
            double[] exp = data.HostileOfRarity(CreatureRarity.Normal)
                .Select(t => (Template: t, Range: FightFactory.LevelRange(t)))
                .Select(x => (double)data.CreatureStats.Derive(x.Template, (ushort)Math.Clamp(level, x.Range.Min, x.Range.Max)).Experience)
                .ToArray();
            double average = exp.Length > 0 ? exp.Average() : 0;
            Add(null, "levelling pace", $"L{level} kills per level", average > 0 ? needed / average : null, g.KillsPerLevel, "");
        }

        return new GradeReport(metrics);
    }

    /// <summary>Where scenarios.json names the build; one it does not name sorts last.</summary>
    private static int BuildOrder(ScenarioFile scenarios, string build)
    {
        int index = Array.IndexOf(scenarios.Builds, build);
        return index < 0 ? int.MaxValue : index;
    }
}
