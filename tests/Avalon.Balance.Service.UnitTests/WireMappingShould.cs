using System.Text.Json;
using Avalon.Balance.Contract;
using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Balance.Service.Mapping;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.Service.UnitTests;

public class WireMappingShould
{
    private static string BalanceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Avalon.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Avalon.sln not found"), "balance");
    }

    private static BalanceConfig Config()
    {
        string dir = BalanceDir();
        return new BalanceConfig(
            ConfigFileStore.Load(Path.Combine(dir, "scenarios.json"), ConfigFiles.ParseScenarios),
            ConfigFileStore.Load(Path.Combine(dir, "targets.json"), ConfigFiles.ParseTargets),
            ConfigFileStore.Load(Path.Combine(dir, "rotations.json"), ConfigFiles.ParseRotations));
    }

    private static RunResult SmallRun() =>
        Simulation.Run(SeedSource.Load(), Config(),
            new RunRequest(null, null,
                new RunFilter(new HashSet<CharacterClass> { CharacterClass.Warrior }, null, null, new HashSet<string> { "normal-3" }),
                RunsPerRow: 10, Seed: 1),
            null, CancellationToken.None);

    [Fact]
    public void Map_a_real_run_with_ids_enum_strings_and_summary_intact()
    {
        RunResult result = SmallRun();
        Assert.Equal(RunStatus.Done, result.Status);

        RunResultDto dto = WireMapping.ToDto(result);

        Assert.Equal(result.Rows.Count, dto.Rows.Count);
        RowResult first = result.Rows[0];
        RowKey k = first.Key;
        Assert.Equal($"{k.Class}|{k.Level}|{k.Gear}|{k.Scenario}", dto.Rows[0].Id);
        Assert.Equal(k.Class.ToString(), dto.Rows[0].Class);
        Assert.Equal(first.WinRatePct, dto.Rows[0].WinRatePct);
        Assert.Equal(first.FightSeconds.Median, dto.Rows[0].FightSeconds.Median);
        Assert.Equal(result.CheckedRows.Select(WireMapping.RowId), dto.CheckedRows);
        Assert.Equal(result.Summary.Green, dto.Summary.Green);
        Assert.Equal(result.Summary.Yellow, dto.Summary.Yellow);
        Assert.Equal(result.Summary.Red, dto.Summary.Red);
        Assert.Equal(result.Grades.Metrics.Count, dto.Metrics.Count);
        Assert.All(dto.Metrics, m => Assert.Contains(m.Grade, new[] { "Green", "Yellow", "Red" }));
        Assert.Equal(result.Seed, dto.Seed);
        Assert.Equal(result.RunsPerRow, dto.RunsPerRow);
    }

    [Fact]
    public void Serialise_without_NaN_and_with_enums_as_strings()
    {
        var bad = new GradedMetric(null, "check", "metric", double.NaN, new Band { Min = 1, Max = 2 }, "%", Grade.Red);
        RunResult result = SmallRun() with
        {
            Grades = new GradeReport([bad, bad with { Value = double.PositiveInfinity }]),
        };

        string json = JsonSerializer.Serialize(WireMapping.ToDto(result), BalanceJson.Options);

        Assert.DoesNotContain("NaN", json);
        Assert.DoesNotContain("Infinity", json);
        Assert.Contains("\"grade\":\"Red\"", json);
        Assert.Contains("\"value\":null", json);
        Assert.Contains("\"winRatePct\"", json);
    }

    [Fact]
    public void Map_a_NaN_metric_value_to_null()
    {
        var metric = new GradedMetric(null, "c", "m", double.NaN, new Band { Min = double.NaN, Max = 2 }, "%", Grade.Red);

        MetricDto dto = WireMapping.ToDto(metric);

        Assert.Null(dto.Value);
        Assert.Null(dto.RowId);
        Assert.Null(dto.Band.Min);
        Assert.Equal(2, dto.Band.Max);
    }

    [Fact]
    public void Map_a_filter_to_the_core_filter()
    {
        (RunFilter? filter, IReadOnlyList<IssueDto> issues) =
            WireMapping.ToFilter(new RunFilterDto(["Warrior"], [5], null, null));

        Assert.Empty(issues);
        Assert.NotNull(filter);
        Assert.Equal(new HashSet<CharacterClass> { CharacterClass.Warrior }, filter.Classes);
        Assert.Equal(new HashSet<ushort> { 5 }, filter.Levels);
        Assert.Null(filter.Gear);
        Assert.Null(filter.Scenarios);
    }

    [Fact]
    public void Keep_everything_when_there_is_no_filter()
    {
        (RunFilter? filter, IReadOnlyList<IssueDto> issues) = WireMapping.ToFilter(null);

        Assert.Empty(issues);
        Assert.Equal(RunFilter.None, filter);
    }

    [Fact]
    public void Refuse_an_unknown_class_on_filter_classes()
    {
        (RunFilter? filter, IReadOnlyList<IssueDto> issues) = WireMapping.ToFilter(new RunFilterDto(["Nobody"], null, null, null));

        Assert.Null(filter);
        Assert.Equal("filter.classes", Assert.Single(issues).Path);
    }

    [Fact]
    public void Refuse_a_level_outside_ushort_on_filter_levels()
    {
        (_, IReadOnlyList<IssueDto> issues) = WireMapping.ToFilter(new RunFilterDto(null, [70000], null, null));

        Assert.Equal("filter.levels", Assert.Single(issues).Path);
    }

    [Fact]
    public void Round_trip_the_checked_in_config()
    {
        BalanceConfigDto dto = WireMapping.ToDto(Config());

        (BalanceConfig? config, IReadOnlyList<IssueDto> issues) = WireMapping.ToConfig(dto);

        Assert.Empty(issues);
        Assert.NotNull(config);
        Assert.Equal(ConfigFiles.Save(Config()), ConfigFiles.Save(config));
    }

    [Fact]
    public void Refuse_invalid_scenarios_json_on_config_scenarios()
    {
        BalanceConfigDto good = WireMapping.ToDto(Config());

        (BalanceConfig? config, IReadOnlyList<IssueDto> issues) = WireMapping.ToConfig(good with { Scenarios = "{ not json" });

        Assert.Null(config);
        Assert.Equal("config.scenarios", Assert.Single(issues).Path);
    }

    [Fact]
    public void Report_every_bad_file_at_once()
    {
        (_, IReadOnlyList<IssueDto> issues) = WireMapping.ToConfig(new BalanceConfigDto("[]", "[]", "[]"));

        Assert.Equal(["config.scenarios", "config.targets", "config.rotations"], issues.Select(i => i.Path));
    }
}
