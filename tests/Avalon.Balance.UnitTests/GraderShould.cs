using Avalon.Balance;
using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class GraderShould
{
    private static readonly string BalanceDir = Path.Combine(RepositoryRoot.Find(), "balance");
    private static readonly PlayerSnapshot NoSnapshot = new(1, 1, 0, 0, 0, 0, 0, 0, 0, []);

    private static ScenarioFile Scenarios() => ConfigFileStore.Load(Path.Combine(BalanceDir, "scenarios.json"), ConfigFiles.ParseScenarios);

    private static TargetFile Targets() => ConfigFileStore.Load(Path.Combine(BalanceDir, "targets.json"), ConfigFiles.ParseTargets);

    private static RowResult Row(CharacterClass c, ushort level, string scenario, double win, double fight,
        double? health = 50, double? firstSpender = 1, double starved = 0) =>
        new(new RowKey(c, level, "forest", scenario), 100, win, new Distribution(fight, fight, fight),
            health is { } h ? new Distribution(h, h, h) : null,
            firstSpender is { } s ? new Distribution(s, s, s) : null, new Distribution(starved, starved, starved),
            new Dictionary<string, double>(), new Dictionary<string, double>(), NoSnapshot);

    [Theory]
    [InlineData(90d, Grade.Green)]
    [InlineData(80d, Grade.Green)]
    [InlineData(70d, Grade.Yellow)]    // 10 below 80, within 15 % of 80 (12)
    [InlineData(60d, Grade.Red)]
    [InlineData(97d, Grade.Yellow)]    // 2 above 95, within 14.25
    public void Grade_a_value_against_a_band(double value, Grade expected) =>
        Assert.Equal(expected, new Band { Min = 80, Max = 95 }.Grade(value, 15));

    [Fact]
    public void Grade_a_missing_value_red()
    {
        Assert.Equal(Grade.Red, new Band { Min = 10 }.Grade(null, 15));

        GradeReport report = Grader.Grade([Row(CharacterClass.Warrior, 1, "elite-1", win: 0, fight: 12, health: null)],
            TestData.Seeded, Scenarios(), Targets());

        GradedMetric health = report.For(new RowKey(CharacterClass.Warrior, 1, "forest", "elite-1"), "health left")!;
        Assert.Null(health.Value);
        Assert.Equal(Grade.Red, health.Grade);
    }

    [Fact]
    public void Grade_only_the_graded_gear()
    {
        RowResult starter = Row(CharacterClass.Warrior, 1, "normal-1", 0, 100) with
        {
            Key = new RowKey(CharacterClass.Warrior, 1, "starter", "normal-1"),
        };

        GradeReport report = Grader.Grade([starter], TestData.Seeded, Scenarios(), Targets());

        Assert.DoesNotContain(report.Metrics, m => m.Row == starter.Key);
    }

    [Fact]
    public void Flag_a_curve_that_is_not_flat_across_levels()
    {
        RowResult[] rows = [Row(CharacterClass.Warrior, 1, "normal-3", 100, 20), Row(CharacterClass.Warrior, 2, "normal-3", 60, 20)];

        GradeReport report = Grader.Grade(rows, TestData.Seeded, Scenarios(), Targets());

        GradedMetric flat = report.Metrics.Single(m => m.Check == "flat curve" && m.Metric == "Warrior normal-3 win rate spread");
        Assert.Equal(40d, flat.Value);
        Assert.Equal(Grade.Red, flat.Grade);
    }

    [Fact]
    public void Flag_a_class_far_from_the_class_average()
    {
        RowResult[] rows =
        [
            Row(CharacterClass.Warrior, 1, "normal-3", 100, 20), Row(CharacterClass.Wizard, 1, "normal-3", 100, 20),
            Row(CharacterClass.Hunter, 1, "normal-3", 100, 20), Row(CharacterClass.Healer, 1, "normal-3", 60, 20),
        ];

        GradeReport report = Grader.Grade(rows, TestData.Seeded, Scenarios(), Targets());

        GradedMetric healer = report.Metrics.Single(m => m.Check == "class parity" && m.Metric == "Healer L1 win rate vs average");
        Assert.Equal(-30d, healer.Value);
        Assert.Equal(Grade.Red, healer.Grade);
    }

    [Fact]
    public void Check_resource_flow_for_the_warrior_and_the_casters()
    {
        RowResult[] rows =
        [
            Row(CharacterClass.Warrior, 1, "normal-3", 100, 20, firstSpender: 6),
            Row(CharacterClass.Wizard, 1, "normal-3", 100, 20, starved: 25),
        ];

        GradeReport report = Grader.Grade(rows, TestData.Seeded, Scenarios(), Targets());

        Assert.Equal(Grade.Red, report.Metrics.Single(m => m.Metric == "Warrior L1 first spender").Grade);
        Assert.Equal(Grade.Red, report.Metrics.Single(m => m.Metric == "Wizard L1 starved share").Grade);
    }

    [Fact]
    public void Grade_resource_flow_from_the_median_of_the_runs()
    {
        RowResult[] rows =
        [
            Row(CharacterClass.Warrior, 1, "normal-3", 100, 20) with { FirstSpenderSeconds = new Distribution(1, 3, 9) },
            Row(CharacterClass.Warrior, 2, "normal-3", 100, 20) with { FirstSpenderSeconds = new Distribution(0.5, 6, 6) },
            Row(CharacterClass.Wizard, 1, "normal-3", 100, 20) with { StarvedPct = new Distribution(0, 5, 40) },
        ];

        GradeReport report = Grader.Grade(rows, TestData.Seeded, Scenarios(), Targets());

        GradedMetric fast = report.Metrics.Single(m => m.Metric == "Warrior L1 first spender");
        Assert.Equal(3, fast.Value);
        Assert.Equal(Grade.Green, fast.Grade);
        Assert.Equal(Grade.Red, report.Metrics.Single(m => m.Metric == "Warrior L2 first spender").Grade);
        GradedMetric starved = report.Metrics.Single(m => m.Metric == "Wizard L1 starved share");
        Assert.Equal(5, starved.Value);
        Assert.Equal(Grade.Green, starved.Grade);
    }

    [Fact]
    public void Count_same_level_normal_kills_per_level()
    {
        GradeReport report = Grader.Grade([], TestData.Seeded, Scenarios(), Targets());

        GradedMetric level1 = report.Metrics.Single(m => m.Check == "levelling pace" && m.Metric == "L1 kills per level");
        Assert.NotNull(level1.Value);
        Assert.True(level1.Value > 0);
    }

    [Fact]
    public void Refuse_a_negative_yellow_tolerance()
    {
        TargetFile targets = Targets();
        targets.YellowTolerancePct = -1;

        Assert.Contains("yellowTolerancePct",
            Assert.Throws<InvalidDataException>(() => Grader.Grade([], TestData.Seeded, Scenarios(), targets)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Say_any_red_and_list_the_worst_first()
    {
        GradeReport report = Grader.Grade([Row(CharacterClass.Warrior, 1, "normal-1", win: 10, fight: 60)],
            TestData.Seeded, Scenarios(), Targets());

        Assert.True(report.AnyRed);
        Assert.Equal(Grade.Red, report.Worst(1).Single().Grade);
    }
}
