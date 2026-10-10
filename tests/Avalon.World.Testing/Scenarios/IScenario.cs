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

    /// <summary>
    /// Null, the default, for a scenario in a steady state, measured after a wall-clock warm-up by the least of
    /// <see cref="ScenarioMeasurement.Windows" /> windows. A scenario whose world keeps changing (a fight: creatures die,
    /// players die and are revived, loot drops) names its fixed length instead, and is measured over exactly those
    /// ticks of a fresh world, so every machine measures the same stretch of the same fight; see
    /// <see cref="ScenarioMeasurement.Run" />.
    /// </summary>
    FixedLength? Length => null;
}

/// <summary>
/// A scenario run of a fixed length: <see cref="WarmupTicks" /> ticks from the world's build, then
/// <see cref="MeasuredTicks" /> measured ticks, a whole number of <see cref="ScenarioMeasurement.WindowTicks" />-tick
/// windows.
/// </summary>
public sealed record FixedLength
{
    public FixedLength(int warmupTicks, int measuredTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(warmupTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(measuredTicks);
        if (measuredTicks % ScenarioMeasurement.WindowTicks != 0)
        {
            throw new ArgumentException(
                $"The measured ticks must be whole windows of {ScenarioMeasurement.WindowTicks}", nameof(measuredTicks));
        }

        WarmupTicks = warmupTicks;
        MeasuredTicks = measuredTicks;
    }

    /// <summary>Ticks from the world's build to the first measured one.</summary>
    public int WarmupTicks { get; }

    /// <summary>Ticks measured: whole windows.</summary>
    public int MeasuredTicks { get; }

    /// <summary>The measured windows.</summary>
    public int Windows => MeasuredTicks / ScenarioMeasurement.WindowTicks;
}
