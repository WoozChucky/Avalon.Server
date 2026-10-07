namespace Avalon.Balance.Core;

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

    /// <summary>Checks the targets against the scenarios they grade.</summary>
    /// <exception cref="InvalidDataException">The first problem, naming the entry.</exception>
    public void Validate(ScenarioFile scenarios)
    {
        if (Scenarios is null) throw new InvalidDataException("targets: scenarios is missing");
        if (Global is null) throw new InvalidDataException("targets: global is missing");
        if (Global.KillsPerLevel is null) throw new InvalidDataException("targets: global killsPerLevel is missing");
        if (GradedGear is null) throw new InvalidDataException("targets: gradedGear is missing");

        var ids = scenarios.Scenarios.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        if (!scenarios.Gear.Contains(GradedGear, StringComparer.Ordinal))
            throw new InvalidDataException(
                $"targets: gradedGear '{GradedGear}' is not one of the scenarios' gear ({string.Join(", ", scenarios.Gear)})");

        foreach ((string id, ScenarioTargets targets) in Scenarios)
        {
            if (!ids.Contains(id))
                throw new InvalidDataException($"targets: scenario '{id}' is not in scenarios.json");
            if (targets is null) throw new InvalidDataException($"targets: scenario '{id}' has no targets");
            CheckBand(targets.WinRate, $"scenario '{id}' winRate");
            CheckBand(targets.FightSeconds, $"scenario '{id}' fightSeconds");
            CheckBand(targets.HealthLeftPct, $"scenario '{id}' healthLeftPct");
        }

        if (!ids.Contains(Global.ParityScenario))
            throw new InvalidDataException($"targets: global parityScenario '{Global.ParityScenario}' is not in scenarios.json");
        if (!ids.Contains(Global.ResourceScenario))
            throw new InvalidDataException($"targets: global resourceScenario '{Global.ResourceScenario}' is not in scenarios.json");
        CheckBand(Global.KillsPerLevel, "global killsPerLevel");

        double tol = YellowTolerancePct;
        if (!(tol >= 0d) || double.IsInfinity(tol))
            throw new InvalidDataException($"targets: yellowTolerancePct must be a finite 0 or more, not {tol}");
    }

    private static void CheckBand(Band? band, string name)
    {
        if (band is { Min: { } lo, Max: { } hi } && lo > hi)
            throw new InvalidDataException($"targets: {name} has min {lo} above max {hi}");
    }
}
