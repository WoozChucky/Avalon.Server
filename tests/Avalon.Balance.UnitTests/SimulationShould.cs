using System.Text.Json;
using Avalon.Balance.Core;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class SimulationShould
{
    private static RunRequest Quick(RunFilter? filter = null, string? overrides = null) =>
        new(overrides is null ? null : JsonDocument.Parse(overrides).RootElement, null,
            filter ?? new RunFilter(new HashSet<CharacterClass> { CharacterClass.Warrior }, null, null, new HashSet<string> { "normal-3" }),
            RunsPerRow: 20, Seed: 1);

    private static RunFilter AllWarriorRows => new(new HashSet<CharacterClass> { CharacterClass.Warrior }, null, null, null);

    [Fact]
    public void Give_the_same_rows_as_the_command_line_runner()
    {
        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick(), null, CancellationToken.None);
        // Same plan through the old path.
        var runner = new BalanceRunner(TestData.Seeded, TestData.Config().Scenarios, TestData.Config().Rotations);
        var expected = runner.Run(runner.Plan(CharacterClass.Warrior, "normal-3", 20, 1));

        Assert.Equal(RunStatus.Done, result.Status);
        Assert.Equal(expected.Select(r => (r.Key, r.WinRatePct)), result.Rows.Select(r => (r.Key, r.WinRatePct)));
    }

    [Fact]
    public void Filter_by_level_and_gear_without_changing_a_rows_numbers()
    {
        RunResult all = Simulation.Run(TestData.Seed(), TestData.Config(), Quick(), null, CancellationToken.None);
        RunResult some = Simulation.Run(TestData.Seed(), TestData.Config(),
            Quick(new RunFilter(new HashSet<CharacterClass> { CharacterClass.Warrior }, new HashSet<ushort> { 5 }, new HashSet<string> { "forest" }, new HashSet<string> { "normal-3" })),
            null, CancellationToken.None);

        RowResult row = Assert.Single(some.Rows);
        Assert.Equal(all.Rows.Single(r => r.Key == row.Key).WinRatePct, row.WinRatePct);
    }

    [Fact]
    public void Report_the_rows_the_global_checks_cover()
    {
        RunResult some = Simulation.Run(TestData.Seed(), TestData.Config(),
            Quick(new RunFilter(new HashSet<CharacterClass> { CharacterClass.Warrior }, null, new HashSet<string> { "none", "forest" }, new HashSet<string> { "normal-3" })),
            null, CancellationToken.None);
        string graded = TestData.Config().Targets.GradedGear;

        Assert.Contains(some.Rows, r => r.Key.Gear != graded);
        Assert.NotEmpty(some.CheckedRows);
        Assert.Equal(some.Rows.Select(r => r.Key).Where(k => k.Gear == graded), some.CheckedRows);
        Assert.All(some.CheckedRows, k => Assert.Equal(graded, k.Gear));
    }

    [Fact]
    public void Summarise_the_grades_and_echo_the_seed_and_runs()
    {
        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick(), null, CancellationToken.None);

        Assert.Equal(new RunSummary(result.Grades.Count(Grade.Green), result.Grades.Count(Grade.Yellow), result.Grades.Count(Grade.Red)), result.Summary);
        Assert.Equal(1, result.Seed);
        Assert.Equal(20, result.RunsPerRow);
    }

    [Fact]
    public void Report_a_bad_override_as_an_issue_and_leave_the_seed_alone()
    {
        SeedTables seed = TestData.Seed();
        RunResult result = Simulation.Run(seed, TestData.Config(), Quick(overrides: """{ "CombatFormula.NoSuchColumn": 1 }"""), null, CancellationToken.None);

        Assert.Equal(RunStatus.Invalid, result.Status);
        Assert.Contains(result.Issues, i => i.Path.StartsWith("overrides.", StringComparison.Ordinal));
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Name_the_offending_override_key_in_the_issue_path()
    {
        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick(overrides: """{ "CombatFormula.NoSuchColumn": 1 }"""), null, CancellationToken.None);

        Assert.Equal("overrides.CombatFormula.NoSuchColumn", Assert.Single(result.Issues).Path);
    }

    [Fact]
    public void Apply_overrides_to_a_copy_and_never_touch_the_callers_seed()
    {
        SeedTables seed = TestData.Seed();
        var before = seed.AbilityTemplates.Single(a => a.Id.Value == 201).EffectValue;

        RunResult result = Simulation.Run(seed, TestData.Config(), Quick(overrides: """{ "Ability.201.EffectValue": 18 }"""), null, CancellationToken.None);

        Assert.Equal(RunStatus.Done, result.Status);
        Assert.Equal("Ability.201.EffectValue", Assert.Single(result.Overrides.Applied).Key);
        Assert.Equal(before, seed.AbilityTemplates.Single(a => a.Id.Value == 201).EffectValue);
    }

    [Fact]
    public void Clone_rows_deeply()
    {
        SeedTables original = TestData.Seed();
        SeedTables copy = original.Clone();
        var before = original.AbilityTemplates.Single(a => a.Id.Value == 201).EffectValue;

        Overrides.Apply(copy, JsonDocument.Parse("""{ "Ability.201.EffectValue": 18 }""").RootElement);

        Assert.Equal(18u, copy.AbilityTemplates.Single(a => a.Id.Value == 201).EffectValue);
        Assert.Equal(before, original.AbilityTemplates.Single(a => a.Id.Value == 201).EffectValue);
        Assert.NotSame(original.AbilityTemplates, copy.AbilityTemplates);
        Assert.Equal(original.ItemTemplates.Count, copy.ItemTemplates.Count);
    }

    [Fact]
    public void Report_a_bad_config_as_an_issue()
    {
        BalanceConfig config = TestData.Config();
        var broken = config with { Rotations = new RotationFile { [CharacterClass.Warrior] = [new RotationEntry { Ability = 99999 }] } };

        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick() with { Config = broken }, null, CancellationToken.None);

        Assert.Equal(RunStatus.Invalid, result.Status);
        Assert.Contains(result.Issues, i => i.Path.StartsWith("rotations", StringComparison.Ordinal));
    }

    [Fact]
    public void Report_a_negative_yellow_tolerance_as_a_targets_issue()
    {
        BalanceConfig config = TestData.Config();
        config.Targets.YellowTolerancePct = -1;

        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick() with { Config = config }, null, CancellationToken.None);

        Assert.Equal(RunStatus.Invalid, result.Status);
        Issue issue = Assert.Single(result.Issues);
        Assert.Equal("targets", issue.Path);
        Assert.Contains("yellowTolerancePct", issue.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("filter.levels")]
    [InlineData("filter.gear")]
    [InlineData("filter.scenarios")]
    [InlineData("filter.classes")]
    public void Report_an_unknown_filter_value_as_an_issue(string path)
    {
        HashSet<CharacterClass> classes = path == "filter.classes" ? [(CharacterClass)250] : [CharacterClass.Warrior];
        HashSet<ushort>? levels = path == "filter.levels" ? [999] : null;
        HashSet<string>? gear = path == "filter.gear" ? ["nope"] : null;
        HashSet<string> scenarios = path == "filter.scenarios" ? ["nope"] : ["normal-3"];

        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick(new RunFilter(classes, levels, gear, scenarios)), null, CancellationToken.None);

        Assert.Equal(RunStatus.Invalid, result.Status);
        Assert.Equal(path, Assert.Single(result.Issues).Path);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Stop_when_cancelled_and_return_no_rows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick(), null, cts.Token);

        Assert.Equal(RunStatus.Cancelled, result.Status);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Stop_when_cancelled_part_way_through_and_return_no_rows()
    {
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<RunProgress>(_ => cts.Cancel());

        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick(AllWarriorRows) with { RunsPerRow = 200 }, progress, cts.Token);

        Assert.Equal(RunStatus.Cancelled, result.Status);
        Assert.Empty(result.Rows);
        Assert.Empty(result.CheckedRows);
    }

    [Fact]
    public void Report_progress_up_to_every_row()
    {
        var seen = new List<RunProgress>();
        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick(), new SyncProgress<RunProgress>(seen.Add), CancellationToken.None);

        Assert.Equal(new RunProgress(result.Rows.Count, result.Rows.Count), seen.MaxBy(p => p.RowsDone));
    }

    [Fact]
    public void Report_progress_in_increasing_order_from_parallel_rows()
    {
        var seen = new List<RunProgress>();

        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick(AllWarriorRows), new SyncProgress<RunProgress>(seen.Add), CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, result.Rows.Count), seen.Select(p => p.RowsDone));
        Assert.All(seen, p => Assert.Equal(result.Rows.Count, p.RowsTotal));
    }

    [Fact]
    public void Report_levels_past_the_seeded_class_stats_as_an_issue()
    {
        BalanceConfig config = TestData.Config();
        config.Scenarios.Levels = [1, 20];

        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick() with { Config = config }, null, CancellationToken.None);

        Assert.Equal(RunStatus.Invalid, result.Status);
        Issue issue = Assert.Single(result.Issues);
        Assert.Equal("scenarios", issue.Path);
        Assert.Contains("Warrior", issue.Message, StringComparison.Ordinal);
        Assert.Contains("level 17", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Treat_a_null_filter_as_no_filter()
    {
        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick() with { Filter = null! }
            with
        { RunsPerRow = 1 }, null, CancellationToken.None);

        Assert.Equal(RunStatus.Done, result.Status);
        Assert.Equal(TestData.Config().Scenarios.Classes.Length * 10 * 3 * 8, result.Rows.Count);
    }

    [Fact]
    public void Carry_on_when_the_progress_handler_throws()
    {
        int calls = 0;
        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick(),
            new SyncProgress<RunProgress>(_ => { calls++; throw new InvalidOperationException("handler bug"); }), CancellationToken.None);

        Assert.Equal(RunStatus.Done, result.Status);
        Assert.Equal(30, result.Rows.Count);
        Assert.Equal(30, calls);
    }

    [Fact]
    public void Return_no_rows_when_cancelled_as_the_last_row_finishes()
    {
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<RunProgress>(p => { if (p.RowsDone == p.RowsTotal) cts.Cancel(); });

        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), Quick(), progress, cts.Token);

        Assert.Equal(RunStatus.Cancelled, result.Status);
        Assert.Empty(result.Rows);
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
