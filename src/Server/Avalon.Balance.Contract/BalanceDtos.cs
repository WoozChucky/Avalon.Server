using System.Text.Json;

namespace Avalon.Balance.Contract;

public sealed record CatalogDto(
    string Version,
    string Commit,
    IReadOnlyList<TunableDto> Tunables,
    BalanceConfigDto DefaultConfig,
    IReadOnlyList<string> Classes,
    IReadOnlyList<int> Levels,
    IReadOnlyList<string> Gear,
    IReadOnlyList<string> Scenarios,
    IReadOnlyList<NamedIdDto> Abilities,
    IReadOnlyList<NamedIdDto> Creatures);

public sealed record TunableDto(string Key, string Table, string? RowKey, string Column, string Type, string SeedValue, string Display);

public sealed record NamedIdDto(long Id, string Name);

/// <summary>Each member is the canonical JSON text of that file.</summary>
public sealed record BalanceConfigDto(string Scenarios, string Targets, string Rotations);

public sealed record RunRequestDto(
    Dictionary<string, JsonElement>? Overrides,
    BalanceConfigDto? Config,
    RunFilterDto? Filter,
    int? RunsPerRow,
    int? Seed);

public sealed record RunFilterDto(string[]? Classes, int[]? Levels, string[]? Gear, string[]? Scenarios);

public sealed record RunAcceptedDto(string RunId);

/// <summary>Status is one of queued, running, done, invalid, failed or cancelled.</summary>
public sealed record RunStatusDto(
    string RunId,
    string Status,
    int RowsDone,
    int RowsTotal,
    RunResultDto? Result,
    IReadOnlyList<IssueDto> Issues);

public sealed record RunResultDto(
    IReadOnlyList<RowDto> Rows,
    IReadOnlyList<MetricDto> Metrics,
    IReadOnlyList<string> CheckedRows,
    SummaryDto Summary,
    IReadOnlyList<AppliedOverrideDto> Applied,
    IReadOnlyList<string> Stale,
    int Seed,
    int RunsPerRow);

/// <summary>Id is "Class|Level|Gear|Scenario".</summary>
public sealed record RowDto(
    string Id,
    string Class,
    int Level,
    string Gear,
    string Scenario,
    double WinRatePct,
    DistributionDto FightSeconds,
    DistributionDto HealthLeftPct,
    IReadOnlyDictionary<string, double> DamageDealtPerRun,
    IReadOnlyDictionary<string, double> DamageTakenPerRun);

public sealed record DistributionDto(double? P10, double? Median, double? P90);

public sealed record MetricDto(string? RowId, string Check, string Metric, double? Value, BandDto Band, string Unit, string Grade);

public sealed record BandDto(double? Min, double? Max);

public sealed record SummaryDto(int Green, int Yellow, int Red);

public sealed record AppliedOverrideDto(string Key, string Seed, string Value);

public sealed record IssueDto(string Path, string Message);

public sealed record ExportRequestDto(
    string Title,
    string? Notes,
    Dictionary<string, JsonElement> Overrides,
    BalanceConfigDto? Config,
    string? RunId);

public sealed record ExportResultDto(string PullRequestUrl, string Branch);
