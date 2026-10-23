namespace Avalon.Combat;

/// <summary>
/// The whole-number maths of an item's local lines (#1179, A-R13): every input is a whole number, so each result is
/// worked out in integers and rounded exactly half away from zero, never through a double that can land just below a
/// half.
/// </summary>
internal static class LocalScaling
{
    /// <summary>
    /// <paramref name="value" /> times <c>max(0, 100 + increasedPct) / 100</c>: a piece's defence, a weapon's damage end.
    /// </summary>
    public static long Increase(long value, long increasedPct) =>
        RoundHalfAway(value * Math.Max(0L, 100L + increasedPct), 100L);

    /// <summary>A uint end increased by <paramref name="increasedPct" />, held to 0..<see cref="uint.MaxValue" />.</summary>
    public static uint Increase(uint value, long increasedPct) => ToUInt(Increase((long)value, increasedPct));

    /// <summary>
    /// <paramref name="timeMs" /> divided by <c>1 + speedPct / 100</c>, at least 1; a speed of 0 or below leaves it as it is.
    /// </summary>
    public static uint Hasten(uint timeMs, long speedPct) =>
        speedPct > 0 ? Math.Max(1u, ToUInt(RoundHalfAway(timeMs * 100L, 100L + speedPct))) : timeMs;

    /// <summary><paramref name="numerator" /> / <paramref name="denominator" /> (above 0), rounded exactly half away from zero.</summary>
    public static long RoundHalfAway(long numerator, long denominator) =>
        numerator >= 0
            ? ((2 * numerator) + denominator) / (2 * denominator)
            : -(((-2 * numerator) + denominator) / (2 * denominator));

    private static uint ToUInt(long value) => (uint)Math.Clamp(value, 0L, uint.MaxValue);
}
