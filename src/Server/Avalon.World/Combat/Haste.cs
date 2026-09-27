namespace Avalon.World.Combat;

/// <summary>
/// The one haste rule (#627): a time becomes <c>time / (1 + haste / 100)</c>, haste in percentage points and
/// already bounded by whoever owns it (a character's EffectiveHastePct, a creature's SwingInterval). A
/// negative or unreadable value counts as none, so haste only ever shortens a time here.
/// </summary>
public static class Haste
{
    public static TimeSpan Scale(TimeSpan time, float effectivePct) =>
        effectivePct > 0f ? time / (1d + effectivePct / 100d) : time;

    /// <summary>The same over seconds, as cooldowns, cast times and swing intervals are kept.</summary>
    public static float Scale(float seconds, float effectivePct) =>
        effectivePct > 0f ? seconds / (1f + effectivePct / 100f) : seconds;
}
