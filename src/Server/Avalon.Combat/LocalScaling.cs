namespace Avalon.Combat;

/// <summary>
/// The whole-number maths of an item's local lines (#1179, A-R13): every input is a whole number, so each result is
/// worked out in integers and rounded exactly half away from zero, never through a double that can land just below a
/// half. A product that passes a long is worked out in 128 bits and held to the long's range, so no amount of authored
/// gear wraps it; one that fits, every real one, stays in 64 bits.
/// </summary>
internal static class LocalScaling
{
    // The largest product RoundHalfAway rounds by 100 without its doubling passing a long.
    private const long MaxIn64 = (long.MaxValue / 2) - 100;

    /// <summary>
    /// <paramref name="value" /> times <c>max(0, 100 + increasedPct) / 100</c>: a piece's defence, a weapon's damage end, the
    /// character's flat life (increased maximum life); held to the long's range.
    /// </summary>
    public static long Increase(long value, long increasedPct)
    {
        if (increasedPct == 0)
            return value;

        long factor = Math.Max(0L, 100L + increasedPct);
        long high = Math.BigMul(value, factor, out long low);
        // A product well inside a long, every real one, rounds in 64 bits (RoundHalfAway doubles it).
        if (high == low >> 63 && low is > -MaxIn64 and < MaxIn64)
            return RoundHalfAway(low, 100L);

        // Near or past a long: the rare product no real gear makes, in 128 bits.
        (Int128 quotient, Int128 remainder) = Int128.DivRem((Int128)value * factor, 100);
        if (Int128.Abs(remainder) * 2 >= 100)
            quotient += Int128.Sign(quotient + remainder);
        return Saturate(quotient);
    }

    /// <summary>
    /// <paramref name="value" /> times <paramref name="factor" />, a product of Mores (#1181: a talent's more or less
    /// maximum life or mana), rounded half away from zero and held to the long's range; a factor below 0, or not a number,
    /// counts as 0. The factor is a double, so the product is first snapped to a millionth: 5 x 0.7 is 3.4999999999999996
    /// in a double and must round as the 3.5 it means.
    /// </summary>
    public static long More(long value, double factor)
    {
        if (factor == 1d)
            return value;

        if (!(factor > 0d))
            return 0;

        double product = Math.Round(Math.Round(value * factor, 6), MidpointRounding.AwayFromZero);
        return product >= long.MaxValue ? long.MaxValue : product <= long.MinValue ? long.MinValue : (long)product;
    }

    /// <summary><paramref name="a" /> plus <paramref name="b" />, held to the long's range rather than wrapping.</summary>
    public static long Add(long a, long b)
    {
        long sum = unchecked(a + b);
        // Two addends of one sign whose sum has the other wrapped.
        return ((a ^ sum) & (b ^ sum)) < 0 ? (a < 0 ? long.MinValue : long.MaxValue) : sum;
    }

    /// <summary><paramref name="value" /> held to the long's range.</summary>
    public static long Saturate(Int128 value) => (long)Int128.Clamp(value, long.MinValue, long.MaxValue);

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
