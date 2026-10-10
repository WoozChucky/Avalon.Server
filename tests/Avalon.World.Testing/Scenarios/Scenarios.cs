namespace Avalon.World.Testing.Scenarios;

/// <summary>Every scenario the baseline measures, by the name it reports them under.</summary>
public static class Scenarios
{
    /// <summary>Every scenario, in the order the baseline runs them.</summary>
    public static IReadOnlyList<IScenario> All { get; } =
    [
        new TownIdleScenario(),
        new TownWalkScenario(),
        new ManyInstancesScenario(),
        new ForestCombatScenario(),
    ];

    /// <summary>The scenario of that name; throws, naming every scenario, when there is none.</summary>
    public static IScenario Get(string name)
    {
        foreach (IScenario scenario in All)
        {
            if (string.Equals(scenario.Name, name, StringComparison.Ordinal))
                return scenario;
        }

        throw new ArgumentException(
            $"No scenario named '{name}'; the scenarios are {string.Join(", ", All.Select(s => s.Name))}", nameof(name));
    }
}
