namespace Avalon.Balance.Core;

/// <summary>
/// Rows, grades and summary are empty unless <see cref="Status" /> is Done; Issues are empty unless it is Invalid.
/// CheckedRows are the rows the global checks (flat curve, parity, resources) were computed over: the rows of the graded
/// gear. The levelling-pace check reads the level data, not rows.
/// </summary>
public sealed record RunResult(
    RunStatus Status,
    IReadOnlyList<RowResult> Rows,
    GradeReport Grades,
    IReadOnlyList<RowKey> CheckedRows,
    RunSummary Summary,
    OverrideReport Overrides,
    IReadOnlyList<Issue> Issues,
    int Seed,
    int RunsPerRow);
