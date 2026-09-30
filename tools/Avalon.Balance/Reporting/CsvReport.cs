using System.Globalization;
using System.Text;
using Avalon.Balance.Core;

namespace Avalon.Balance.Reporting;

public static class CsvReport
{
    private static readonly string[] Header =
    [
        "class", "level", "gear", "scenario", "runs", "win_rate",
        "fight_p10", "fight_median", "fight_p90",
        "health_left_p10", "health_left_median", "health_left_p90",
        "first_spender_p10", "first_spender_median", "first_spender_p90",
        "starved_pct_p10", "starved_pct_median", "starved_pct_p90",
        "grade_win_rate", "grade_fight_length", "grade_health_left",
    ];

    public static string Render(IReadOnlyList<RowResult> rows, GradeReport grades)
    {
        var sb = new StringBuilder().Append(string.Join(',', Header)).Append('\n');
        foreach (RowResult r in rows)
        {
            string?[] fields =
            [
                r.Key.Class.ToString(), N(r.Key.Level), r.Key.Gear, r.Key.Scenario, N(r.Runs), N(r.WinRatePct),
                N(r.FightSeconds.P10), N(r.FightSeconds.Median), N(r.FightSeconds.P90),
                N(r.HealthLeftPct?.P10), N(r.HealthLeftPct?.Median), N(r.HealthLeftPct?.P90),
                N(r.FirstSpenderSeconds?.P10), N(r.FirstSpenderSeconds?.Median), N(r.FirstSpenderSeconds?.P90),
                N(r.StarvedPct.P10), N(r.StarvedPct.Median), N(r.StarvedPct.P90),
                G(grades.For(r.Key, "win rate")), G(grades.For(r.Key, "fight length")), G(grades.For(r.Key, "health left")),
            ];
            sb.Append(string.Join(',', fields.Select(Escape))).Append('\n');
        }

        return sb.ToString();
    }

    private static string N(double? v) => v is { } d ? d.ToString("0.###", CultureInfo.InvariantCulture) : "";

    private static string G(GradedMetric? m) => m is null ? "" : m.Grade.ToString().ToLowerInvariant();

    private static string Escape(string? field)
    {
        field ??= "";
        return field.IndexOfAny([',', '"', '\n']) >= 0 ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : field;
    }
}
