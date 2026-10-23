namespace Avalon.Combat;

/// <summary>
/// The one speed rule (#627, #1178): a time becomes <c>time / (1 + speed / 100)</c>, the speed in percentage points and
/// already bounded by whoever owns it (<see cref="CasterPace" />, a creature's SwingInterval). A negative or unreadable
/// value counts as none, so a speed only ever shortens a time here.
/// </summary>
public static class Speed
{
    public static TimeSpan Scale(TimeSpan time, float effectivePct) =>
        effectivePct > 0f ? time / (1d + effectivePct / 100d) : time;

    /// <summary>The same over seconds, as cooldowns, cast times and swing intervals are kept.</summary>
    public static float Scale(float seconds, float effectivePct) =>
        effectivePct > 0f ? seconds / (1f + effectivePct / 100f) : seconds;
}
