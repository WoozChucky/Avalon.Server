using Avalon.Balance;
using Avalon.Balance.Config;
using Avalon.Balance.Data;
using Avalon.Balance.Grading;
using Avalon.Balance.Reporting;
using Avalon.Balance.Running;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class ReportsShould
{
    private static readonly string BalanceDir = Path.Combine(RepositoryRoot.Find(), "balance");

    private static RowResult Row(CharacterClass c, ushort level, double win) =>
        new(new RowKey(c, level, "forest", "normal-3"), 10, win, new Distribution(15, 20, 25), new Distribution(40, 50, 60),
            new Distribution(1, 1.5, 2), new Distribution(2, 3, 4.25), new Dictionary<string, double> { ["Cleave, the \"big\" one"] = 120.5 }, new Dictionary<string, double> { ["Grey Fen Wolf: Bite"] = 80 },
            new PlayerSnapshot(300, 100, 50, 4, 20, 5, 3.6f, 5, 3, [new AbilityLine("Cleave", "Damage", 30, 36)]));

    private static ReportContext Context(params RowResult[] rows)
    {
        ScenarioFile scenarios = ConfigFiles.Load(Path.Combine(BalanceDir, "scenarios.json"), ConfigFiles.ParseScenarios);
        TargetFile targets = ConfigFiles.Load(Path.Combine(BalanceDir, "targets.json"), ConfigFiles.ParseTargets);
        GradeReport grades = Grader.Grade(rows, TestData.Seeded, scenarios, targets);
        return new ReportContext(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), "abc1234", 672, 10,
            new OverrideReport([new AppliedOverride("Ability.201.EffectValue", "25", "18")], ["Ability.200.EffectValue"]),
            rows, grades, scenarios, targets);
    }

    [Fact]
    public void Write_one_csv_line_per_row_with_a_header_and_escaped_fields()
    {
        ReportContext ctx = Context(Row(CharacterClass.Warrior, 1, 96), Row(CharacterClass.Wizard, 1, 80));

        string[] lines = CsvReport.Render(ctx.Rows, ctx.Grades).TrimEnd().Split('\n');

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("class,level,gear,scenario,runs,win_rate,", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("Warrior,1,forest,normal-3,10,96,", lines[1], StringComparison.Ordinal);
        Assert.Contains("green", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Write_p10_median_and_p90_for_the_first_spender_and_the_starved_share()
    {
        RowResult noSpender = Row(CharacterClass.Wizard, 1, 80) with { FirstSpenderSeconds = null };
        ReportContext ctx = Context(Row(CharacterClass.Warrior, 1, 96), noSpender);

        string[] lines = CsvReport.Render(ctx.Rows, ctx.Grades).TrimEnd().Split('\n');
        string[] header = lines[0].Split(',');
        string[] warrior = lines[1].Split(',');
        string[] wizard = lines[2].Split(',');
        string At(string[] line, string column) => line[Array.IndexOf(header, column)];

        Assert.Equal(["1", "1.5", "2"],
            new[] { "first_spender_p10", "first_spender_median", "first_spender_p90" }.Select(c => At(warrior, c)));
        Assert.Equal(["2", "3", "4.25"],
            new[] { "starved_pct_p10", "starved_pct_median", "starved_pct_p90" }.Select(c => At(warrior, c)));
        Assert.Equal(["", "", ""],
            new[] { "first_spender_p10", "first_spender_median", "first_spender_p90" }.Select(c => At(wizard, c)));
        Assert.Equal(header.Length, warrior.Length);
    }

    [Fact]
    public void Render_a_self_contained_page_with_every_section()
    {
        string html = HtmlReport.Render(Context(Row(CharacterClass.Warrior, 1, 96), Row(CharacterClass.Warrior, 2, 60)));

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
        foreach (string text in new[] { "abc1234", "Seed 672", "Ability.201.EffectValue", "already applied", "Summary",
                     "normal-3", "Level curves", "<svg", "Cleave, the &quot;big&quot; one", "prefers-color-scheme" })
            Assert.Contains(text, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_a_dash_for_a_missing_value()
    {
        RowResult noWins = Row(CharacterClass.Warrior, 1, 0) with { HealthLeftPct = null, FirstSpenderSeconds = null };

        string html = HtmlReport.Render(Context(noWins));

        Assert.Contains("&ndash;", html, StringComparison.Ordinal);
    }
}
