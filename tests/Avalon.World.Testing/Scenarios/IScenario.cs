namespace Avalon.World.Testing.Scenarios;

/// <summary>A fixed, deterministic world load the baseline measures: its instances, players and creatures.</summary>
public interface IScenario
{
    /// <summary>The name the baseline reports it under.</summary>
    string Name { get; }

    /// <summary>How many players it puts in the world.</summary>
    int Players { get; }

    /// <summary>Builds its instances, players and creatures, ready to tick.</summary>
    ScenarioWorld Build();
}
