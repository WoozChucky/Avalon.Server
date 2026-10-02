namespace Avalon.Balance.Core;

public enum RunStatus
{
    Done,
    Invalid,
    Cancelled,
}

public sealed record RunSummary(int Green, int Yellow, int Red);

/// <summary>A refusal as data: where in the request it is (overrides.Key, seed, targets, rotations.Warrior, filter.levels) and why.</summary>
public sealed record Issue(string Path, string Message);

public sealed record RunProgress(int RowsDone, int RowsTotal);
