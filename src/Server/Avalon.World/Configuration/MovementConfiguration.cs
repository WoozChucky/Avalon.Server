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

    /// <summary>The setting out of range, named; null when it is in range.</summary>
    public string? Problem()
    {
        if (InputCatchUp < TimeSpan.FromMilliseconds(17) || InputCatchUp > TimeSpan.FromSeconds(2))
            return $"Game:Movement:InputCatchUp must be between 00:00:00.017 and 00:00:02 (was {InputCatchUp})";

        return null;
    }
}
