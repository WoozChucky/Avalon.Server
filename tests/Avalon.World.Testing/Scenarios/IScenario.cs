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

    /// <summary>
    /// Checks that the world still did the work the scenario measures over the ticks since
    /// <see cref="ScenarioWorld.MarkProgress" />, which <see cref="ScenarioMeasurement" /> marks before the measured
    /// windows and checks after them: every player still present, and where the scenario moves them, sent to and
    /// walking, and its creatures fighting. Throws <see cref="InvalidOperationException" /> when it did not, since a
    /// scenario that stops working allocates less and would otherwise pass the allocation gate as an improvement.
    /// </summary>
    void Verify(ScenarioWorld world);
}
