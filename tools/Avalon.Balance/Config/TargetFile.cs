namespace Avalon.Balance.Config;

public sealed class ScenarioTargets
{
    public Band? WinRate { get; set; }

    public Band? FightSeconds { get; set; }

    public Band? HealthLeftPct { get; set; }
}

public sealed class GlobalTargets
{
    public double FlatCurveWinRatePoints { get; set; } = 10;
    public double FlatCurveFightLengthPct { get; set; } = 30;
    public string ParityScenario { get; set; } = "normal-3";
    public double ParityWinRatePoints { get; set; } = 10;
    public double ParityFightLengthPct { get; set; } = 15;
    public string ResourceScenario { get; set; } = "normal-3";
    public double WarriorFirstSpenderSeconds { get; set; } = 4;
    public double CasterStarvedPct { get; set; } = 10;
    public Band KillsPerLevel { get; set; } = new() { Min = 10, Max = 20 };
}

public sealed class TargetFile
{
    /// <summary>The gear profile graded; the others are reported ungraded.</summary>
    public string GradedGear { get; set; } = "forest";

    public double YellowTolerancePct { get; set; } = 15;

    public Dictionary<string, ScenarioTargets> Scenarios { get; set; } = new(StringComparer.Ordinal);

    public GlobalTargets Global { get; set; } = new();
}
