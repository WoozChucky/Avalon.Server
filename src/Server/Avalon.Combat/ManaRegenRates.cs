namespace Avalon.Combat;

/// <summary>
/// Mana regained a second, in and out of combat (#1181): a share of the maximum, scaled by the increased mana
/// regeneration worn. Fixed at each stats refresh, so a tick picks one rate and multiplies; nothing pauses it after a
/// cast. A maximum of 0, or increased regeneration at -100 % or below, regenerates nothing.
/// </summary>
public readonly record struct ManaRegenRates(double InCombat, double OutOfCombat)
{
    public static ManaRegenRates Of(RegenConfiguration config, uint maxMana, float increasedManaRegenPct)
    {
        double scale = Math.Max(0d, 1d + increasedManaRegenPct / 100d);
        double perPct = maxMana / 100d * scale;
        return new ManaRegenRates(perPct * config.ManaRegenPctInCombat, perPct * config.ManaRegenPctOutOfCombat);
    }
}
