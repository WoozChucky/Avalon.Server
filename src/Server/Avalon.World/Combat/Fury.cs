namespace Avalon.World.Combat;

/// <summary>The Fury rules that are pure arithmetic (#526).</summary>
public static class Fury
{
    /// <summary>
    /// Fury a character gains from a hit: <c>floor(lost / maxHealth × factor)</c>, where the health
    /// lost is <c>min(damage, healthBefore)</c>, so overkill does not count. 0 with no maximum health;
    /// never wraps past <see cref="uint.MaxValue" />.
    /// </summary>
    public static uint FromDamageTaken(uint damage, uint healthBefore, uint maxHealth, float factor)
    {
        if (maxHealth == 0 || !(factor > 0f)) return 0;

        uint lost = Math.Min(damage, healthBefore);
        double gain = Math.Floor(lost / (double)maxHealth * factor);
        return gain >= uint.MaxValue ? uint.MaxValue : (uint)gain;
    }
}
