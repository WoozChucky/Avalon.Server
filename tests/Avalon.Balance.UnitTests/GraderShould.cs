using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Common;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class GraderShould
{
    private static readonly string s_balanceDir = Path.Combine(RepositoryRoot.Find(), "balance");
    private static readonly PlayerSnapshot s_noSnapshot = new(1, 1, 0, 0, 0, 0, 0, []);

    private static ScenarioFile Scenarios() => ConfigFileStore.Load(Path.Combine(s_balanceDir, "scenarios.json"), ConfigFiles.ParseScenarios);

    private static TargetFile Targets() => ConfigFileStore.Load(Path.Combine(s_balanceDir, "targets.json"), ConfigFiles.ParseTargets);

    /// <summary>A row of <paramref name="build" />, by default the class's own one-build name ("warrior" for a Warrior).</summary>
    private static RowResult Row(CharacterClass c, ushort level, string scenario, double win, double fight,
        double? health = 50, double starved = 0, string? build = null) =>
        new(new RowKey(build ?? Build(c), c, level, "forest", scenario), 100, win, new Distribution(fight, fight, fight),
            health is { } h ? new Distribution(h, h, h) : null,
            new Distribution(1, 1, 1), new Distribution(starved, starved, starved),
            new Dictionary<string, double>(), new Dictionary<string, double>(), s_noSnapshot);

    private static string Build(CharacterClass c) => c.ToString().ToLowerInvariant();

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

        GradedMetric health = report.For(new RowKey("warrior", CharacterClass.Warrior, 1, "forest", "elite-1"), "health left")!;
        Assert.Null(health.Value);
        Assert.Equal(Grade.Red, health.Grade);
    }

    [Fact]
    public void Grade_only_the_graded_gear()
    {
        RowResult starter = Row(CharacterClass.Warrior, 1, "normal-1", 0, 100) with
        {
            Key = new RowKey("warrior", CharacterClass.Warrior, 1, "starter", "normal-1"),
        };

        GradeReport report = Grader.Grade([starter], TestData.Seeded, Scenarios(), Targets());

        Assert.DoesNotContain(report.Metrics, m => m.Row == starter.Key);
    }

    [Fact]
    public void Flag_a_curve_that_is_not_flat_across_levels()
    {
        RowResult[] rows = [Row(CharacterClass.Warrior, 1, "normal-3", 100, 20), Row(CharacterClass.Warrior, 2, "normal-3", 60, 20)];

        GradeReport report = Grader.Grade(rows, TestData.Seeded, Scenarios(), Targets());

        GradedMetric flat = report.Metrics.Single(m => m.Check == "flat curve" && m.Metric == "warrior normal-3 win rate spread");
        Assert.Equal(40d, flat.Value);
        Assert.Equal(Grade.Red, flat.Grade);
    }

    /// <summary>
    /// R-P2-3: each class is scored by its best build against the average of the classes' best, on that build's row. A
    /// worse second build gets no parity metric and leaves the average alone; a better one becomes the class's score.
    /// </summary>
    [Theory]
    [InlineData(null, "cleric", -30d, Grade.Red)]           // one build: 60 against (100 + 100 + 100 + 60) / 4
    [InlineData(20d, "cleric", -30d, Grade.Red)]            // a worse second build changes nothing
    [InlineData(100d, "cleric-second", 0d, Grade.Green)]    // a better second build is the Cleric's score
    public void Score_each_class_by_its_best_build_against_the_average(double? secondClericWin, string scored, double value,
        Grade grade)
    {
        List<RowResult> rows =
        [
            Row(CharacterClass.Warrior, 1, "normal-3", 100, 20), Row(CharacterClass.Wizard, 1, "normal-3", 100, 20),
            Row(CharacterClass.Hunter, 1, "normal-3", 100, 20), Row(CharacterClass.Cleric, 1, "normal-3", 60, 20),
        ];
        if (secondClericWin is { } win)
            rows.Add(Row(CharacterClass.Cleric, 1, "normal-3", win, 20, build: "cleric-second"));

        GradeReport report = Grader.Grade(rows, TestData.Seeded, Scenarios(), Targets());

        GradedMetric cleric = report.Metrics.Single(m => m.Check == "class parity" && m.Metric == "Cleric L1 win rate vs average");
        Assert.Equal(scored, cleric.Row!.Build);
        Assert.Equal(value, cleric.Value);
        Assert.Equal(grade, cleric.Grade);
        Assert.Equal(8, report.Metrics.Count(m => m.Check == "class parity"));   // two per class, on its best build only
    }

    [Fact]
    public void Check_resource_flow_for_every_class()
    {
        RowResult[] rows =
        [
            Row(CharacterClass.Warrior, 1, "normal-3", 100, 20, starved: 25),
            Row(CharacterClass.Wizard, 1, "normal-3", 100, 20, starved: 25),
        ];

        GradeReport report = Grader.Grade(rows, TestData.Seeded, Scenarios(), Targets());

        Assert.Equal(Grade.Red, report.Metrics.Single(m => m.Metric == "warrior L1 starved share").Grade);
        Assert.Equal(Grade.Red, report.Metrics.Single(m => m.Metric == "wizard L1 starved share").Grade);
    }

    [Fact]
    public void Grade_resource_flow_from_the_median_of_the_runs()
    {
        RowResult[] rows =
        [
            Row(CharacterClass.Wizard, 1, "normal-3", 100, 20) with { StarvedPct = new Distribution(0, 5, 40) },
        ];

        GradeReport report = Grader.Grade(rows, TestData.Seeded, Scenarios(), Targets());

        GradedMetric starved = report.Metrics.Single(m => m.Metric == "wizard L1 starved share");
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
            Assert.Throws<InvalidDataException>(() => targets.Validate(Scenarios())).Message,
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
