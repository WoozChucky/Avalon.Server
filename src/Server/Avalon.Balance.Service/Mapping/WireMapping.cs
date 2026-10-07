using System.Text.Json;
using Avalon.Balance.Contract;
using Avalon.Balance.Core;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Service.Mapping;

/// <summary>Converts Avalon.Balance.Core's types to and from the wire DTOs.</summary>
public static class WireMapping
{
    public static string RowId(RowKey key) => $"{key.Class}|{key.Level}|{key.Gear}|{key.Scenario}";

    /// <summary>NaN and Infinity cannot be written as JSON numbers: they become null.</summary>
    public static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;

    public static RunResultDto ToDto(RunResult result) => new(
        result.Rows.Select(ToDto).ToList(),
        result.Grades.Metrics.Select(ToDto).ToList(),
        result.CheckedRows.Select(RowId).ToList(),
        new SummaryDto(result.Summary.Green, result.Summary.Yellow, result.Summary.Red),
        result.Overrides.Applied.Select(a => new AppliedOverrideDto(a.Key, a.Seed, a.Value)).ToList(),
        result.Overrides.Stale,
        result.Seed,
        result.RunsPerRow);

    public static RowDto ToDto(RowResult row) => new(
        RowId(row.Key),
        row.Key.Class.ToString(),
        row.Key.Level,
        row.Key.Gear,
        row.Key.Scenario,
        Finite(row.WinRatePct) ?? 0d,
        ToDto(row.FightSeconds),
        ToDto(row.HealthLeftPct),
        Finite(row.DamageDealtPerRun),
        Finite(row.DamageTakenPerRun));

    /// <summary>A missing distribution (no run won) is all nulls.</summary>
    public static DistributionDto ToDto(Distribution? d) =>
        d is null ? new DistributionDto(null, null, null) : new DistributionDto(Finite(d.P10), Finite(d.Median), Finite(d.P90));

    /// <summary><see cref="GradedMetric.Severity" /> is dropped: it is MaxValue for a missing value, and derivable.</summary>
    public static MetricDto ToDto(GradedMetric m) => new(
        m.Row is { } row ? RowId(row) : null,
        m.Check,
        m.Metric,
        Finite(m.Value),
        new BandDto(Finite(m.Band.Min), Finite(m.Band.Max)),
        m.Unit,
        m.Grade.ToString());

    public static IssueDto ToDto(Issue issue) => new(issue.Path, issue.Message);

    public static TunableDto ToDto(Tunable t) => new(t.Key, t.Table, t.RowKey, t.Column, t.Type, t.SeedValue, t.Display);

    public static BalanceConfigDto ToDto(BalanceConfig config)
    {
        (string scenarios, string targets, string rotations) = ConfigFiles.Save(config);
        return new BalanceConfigDto(scenarios, targets, rotations);
    }

    /// <summary>The filter, or the issues that refuse it. A null DTO, or a null list, keeps everything.</summary>
    public static (RunFilter? Filter, IReadOnlyList<IssueDto> Issues) ToFilter(RunFilterDto? dto)
    {
        if (dto is null)
            return (RunFilter.None, []);

        var issues = new List<IssueDto>();

        HashSet<CharacterClass>? classes = null;
        if (dto.Classes is { } names)
        {
            classes = [];
            foreach (string name in names)
            {
                if (Enum.TryParse(name, ignoreCase: false, out CharacterClass parsed) && Enum.IsDefined(parsed)
                    && !int.TryParse(name, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    classes.Add(parsed);
                }
                else
                {
                    issues.Add(new IssueDto("filter.classes", $"Unknown class '{name}'"));
                }
            }
        }

        HashSet<ushort>? levels = null;
        if (dto.Levels is { } values)
        {
            levels = [];
            foreach (int level in values)
            {
                if (level is >= ushort.MinValue and <= ushort.MaxValue)
                    levels.Add((ushort)level);
                else
                    issues.Add(new IssueDto("filter.levels", $"Level {level} is out of range"));
            }
        }

        if (issues.Count > 0)
            return (null, issues);

        return (new RunFilter(
            classes,
            levels,
            dto.Gear is null ? null : new HashSet<string>(dto.Gear, StringComparer.Ordinal),
            dto.Scenarios is null ? null : new HashSet<string>(dto.Scenarios, StringComparer.Ordinal)), []);
    }

    /// <summary>The config, parsed strictly through ConfigFiles; every bad file is an issue at config.&lt;file&gt;.</summary>
    public static (BalanceConfig? Config, IReadOnlyList<IssueDto> Issues) ToConfig(BalanceConfigDto dto)
    {
        var issues = new List<IssueDto>();
        ScenarioFile? scenarios = Try(dto.Scenarios, ConfigFiles.ParseScenarios, "config.scenarios", issues);
        TargetFile? targets = Try(dto.Targets, ConfigFiles.ParseTargets, "config.targets", issues);
        RotationFile? rotations = Try(dto.Rotations, ConfigFiles.ParseRotations, "config.rotations", issues);

        return issues.Count > 0 || scenarios is null || targets is null || rotations is null
            ? (null, issues)
            : (new BalanceConfig(scenarios, targets, rotations), []);
    }

    private static T? Try<T>(string json, Func<string, T> parse, string path, List<IssueDto> issues)
        where T : class
    {
        try
        {
            return parse(json ?? "");
        }
        catch (Exception e) when (e is InvalidDataException or JsonException)
        {
            issues.Add(new IssueDto(path, e.Message));
            return null;
        }
    }

    private static Dictionary<string, double> Finite(IReadOnlyDictionary<string, double> values) =>
        values.Where(kv => double.IsFinite(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
}
