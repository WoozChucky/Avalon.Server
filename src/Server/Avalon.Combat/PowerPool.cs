namespace Avalon.Combat;

/// <summary>The mana-pool rule that is pure arithmetic (#526, #1181): what a gain adds.</summary>
public static class PowerPool
{
    /// <summary>
    /// The pool's current value after gaining <paramref name="amount" />: capped at <paramref name="max" />, and
    /// unchanged when the unit is dead, the gain is 0, or the pool is already at or above its maximum.
    /// </summary>
    public static uint Gain(bool dead, uint current, uint max, uint amount)
    {
        if (amount == 0 || dead) return current;
        if (current >= max) return current;

        return (uint)Math.Min(max, (ulong)current + amount);
    }
}
