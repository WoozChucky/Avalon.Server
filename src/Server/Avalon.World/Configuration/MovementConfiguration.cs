namespace Avalon.World.Configuration;

/// <summary>
/// Game:Movement (#1267): how far ahead of server time a character's inputs may move it
/// (<see cref="Avalon.World.Characters.MovementBudget" />). Read at startup; out of range, the start stops
/// (<see cref="MovementConfigurationValidation" />).
/// </summary>
public sealed class MovementConfiguration
{
    /// <summary>
    /// Game:Movement:InputCatchUp: the server time of input steps a character may bank, so the inputs that queued behind
    /// a late tick or a network stall still move it; inputs past it are answered standing. 250 ms is 15 steps. At least
    /// one step (00:00:00.017) and at most 2 seconds.
    /// </summary>
    public TimeSpan InputCatchUp { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Game:Movement:ClockDriftAllowancePercent: how much faster than server time the budget refills, in percent, so a
    /// client whose clock runs slightly fast (a machine's clock commonly drifts 10 to 100 parts per million) is never
    /// answered standing, however long it plays. It is also the most a modified client gains: 0.5 % is 0.5 % above its
    /// speed. From 0 to 5.
    /// </summary>
    public double ClockDriftAllowancePercent { get; set; } = 0.5;

    /// <summary>The first setting out of range, named; null when every one is in range.</summary>
    public string? Problem()
    {
        if (InputCatchUp < TimeSpan.FromMilliseconds(17) || InputCatchUp > TimeSpan.FromSeconds(2))
            return $"Game:Movement:InputCatchUp must be between 00:00:00.017 and 00:00:02 (was {InputCatchUp})";

        // Written so NaN fails it too.
        if (!(ClockDriftAllowancePercent >= 0 && ClockDriftAllowancePercent <= 5))
        {
            return "Game:Movement:ClockDriftAllowancePercent must be between 0 and 5 (was " +
                   ClockDriftAllowancePercent.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        return null;
    }
}
