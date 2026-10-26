namespace Avalon.Combat;

/// <summary>
/// Whole points out of a fractional amount. <c>CharacterEntity.Update</c> pays mana and out-of-combat health through it
/// every tick, aura ticks carry their fraction through it (<see cref="AuraRules" />), and the balance simulator pays mana
/// through it, so the server and the simulator cannot drift.
/// </summary>
public static class PowerRegen
{
    /// <summary>
    /// A 1/60 s TimeSpan is truncated to whole ticks, so sixty of them fall a few millionths of a second short of one;
    /// the tolerance keeps that from costing a whole point. What is left may dip that far below 0 and is repaid by the
    /// next step.
    /// </summary>
    private const double WholePointTolerance = 1e-3;

    /// <summary>
    /// Adds <paramref name="earned" /> to <paramref name="carry" />, takes the whole points out of it and returns
    /// them, leaving the fraction for the next step, so a per-second rate holds at any tick rate. Public so
    /// `CharacterEntity` can use it across the assembly boundary.
    /// </summary>
    public static uint TakeWholePoints(double earned, ref double carry)
    {
        carry += earned;

        double owed = Math.Floor(carry + WholePointTolerance);
        if (owed <= 0d)
            return 0;

        uint whole = owed >= uint.MaxValue ? uint.MaxValue : (uint)owed;
        carry -= whole;
        return whole;
    }
}
