using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// The one fold: <c>(base + flat) x max(0, 1 + percent / 100)</c>. A whole-number stat is rounded and never below 0;
/// a percentage stat is not rounded. The caps apply afterwards, where they always did (the hit's chance caps, the
/// haste cap, the movement floor and cap). Pure.
/// </summary>
public static class AuraStats
{
    public static uint Apply(uint value, AuraStatTotals totals, AuraStat stat)
    {
        if (totals.IsEmpty) return value;

        double result = (value + (double)totals.Flat(stat)) * Factor(totals.Percent(stat));
        if (!(result > 0)) return 0u;
        return result >= uint.MaxValue ? uint.MaxValue : (uint)Math.Round(result, MidpointRounding.AwayFromZero);
    }

    public static float Apply(float value, AuraStatTotals totals, AuraStat stat) =>
        totals.IsEmpty ? value : (float)((value + (double)totals.Flat(stat)) * Factor(totals.Percent(stat)));

    /// <summary>A character's derived stats with its auras folded in. Maximum health never falls below 1.</summary>
    public static DerivedCharacterStats Fold(in DerivedCharacterStats s, AuraStatTotals t) => t.IsEmpty ? s : s with
    {
        MaxHealth = s.MaxHealth == 0 ? 0u : Math.Max(1u, Apply(s.MaxHealth, t, AuraStat.MaxHealth)),
        MaxPower = Apply(s.MaxPower, t, AuraStat.MaxPower),
        Armor = Apply(s.Armor, t, AuraStat.Armor),
        AttackDamage = Apply(s.AttackDamage, t, AuraStat.AttackDamage),
        AbilityDamage = Apply(s.AbilityDamage, t, AuraStat.AbilityDamage),
        CritPct = Apply(s.CritPct, t, AuraStat.CritPct),
        DodgePct = Apply(s.DodgePct, t, AuraStat.DodgePct),
        BlockPct = Apply(s.BlockPct, t, AuraStat.BlockPct),
        HastePct = Apply(s.HastePct, t, AuraStat.HastePct),
        MovementSpeedPct = Apply(s.MovementSpeedPct, t, AuraStat.MovementSpeed),
    };

    /// <summary>A creature's attack with its auras: its damage stats and crit. Its natural damage range is unchanged.</summary>
    public static AttackerCombat Fold(in AttackerCombat a, AuraStatTotals t) => t.IsEmpty ? a : a with
    {
        AttackDamage = Apply(a.AttackDamage, t, AuraStat.AttackDamage),
        AbilityDamage = Apply(a.AbilityDamage, t, AuraStat.AbilityDamage),
        CritPct = Apply(a.CritPct, t, AuraStat.CritPct),
    };

    public static DefenderCombat Fold(in DefenderCombat d, AuraStatTotals t) => t.IsEmpty ? d : d with
    {
        Armor = Apply(d.Armor, t, AuraStat.Armor),
        DodgePct = Apply(d.DodgePct, t, AuraStat.DodgePct),
        BlockPct = Apply(d.BlockPct, t, AuraStat.BlockPct),
    };

    private static double Factor(float percent) => Math.Max(0d, 1d + percent / 100d);
}
