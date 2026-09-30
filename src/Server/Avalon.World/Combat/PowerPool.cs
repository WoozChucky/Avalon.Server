using Avalon.Network.Packets.State;

namespace Avalon.World.Combat;

/// <summary>The power-pool rules that are pure arithmetic (#526): what a gain adds and which pool a reset empties.</summary>
public static class PowerPool
{
    /// <summary>
    /// The pool's current value after gaining <paramref name="amount" />: capped at <paramref name="max" />, and
    /// unchanged when the unit is dead, the gain is 0, the pool is already at or above its maximum, or the pool is
    /// not one a cast spends (Mana, Energy or Fury).
    /// </summary>
    public static uint Gain(PowerType pool, bool dead, uint current, uint max, uint amount)
    {
        if (amount == 0 || dead) return current;
        if (pool is not (PowerType.Mana or PowerType.Energy or PowerType.Fury)) return current;
        if (current >= max) return current;

        return (uint)Math.Min(max, (ulong)current + amount);
    }

    /// <summary>
    /// Whether death and every instance transfer empty this pool: Fury does (#526); Mana and Energy keep what they
    /// hold.
    /// </summary>
    public static bool EmptiesOnReset(PowerType pool) => pool == PowerType.Fury;
}
