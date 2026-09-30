namespace Avalon.Balance.Core;

/// <summary>The three balance files, parsed: what a run reads besides the seed.</summary>
public sealed record BalanceConfig(ScenarioFile Scenarios, TargetFile Targets, RotationFile Rotations);
