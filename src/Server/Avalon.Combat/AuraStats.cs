using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>
/// The one fold (#1178): <c>(base + flat) x max(0, 1 + increased / 100) x each (1 + more / 100)</c>, a Flat or Increased
/// times the stacks, a More multiplying once per stack. A whole-number stat is rounded and never below 0;
/// a percentage stat is not rounded. The caps apply afterwards, where they always did (the hit's chance caps, the
/// speed caps, the movement floor and cap). Pure.
/// </summary>
public static class AuraStats
{
    public static uint Apply(uint value, AuraStatTotals totals, AuraStat stat)
    {
        if (totals.IsEmpty) return value;

        double result = (value + (double)totals.Flat(stat)) * Factor(totals, stat);
        if (!(result > 0)) return 0u;
        return result >= uint.MaxValue ? uint.MaxValue : (uint)Math.Round(result, MidpointRounding.AwayFromZero);
    }

    public static float Apply(float value, AuraStatTotals totals, AuraStat stat) =>
        totals.IsEmpty ? value : (float)((value + (double)totals.Flat(stat)) * Factor(totals, stat));

    /// <summary>
    /// A character's derived stats with its auras folded in. Maximum health never falls below 1. The offence block is left as
    /// it is: CharacterStatsCalculator built it with the auras already (CharacterOffence), so folding it here would count
    /// them twice.
    /// </summary>
    public static DerivedCharacterStats Fold(in DerivedCharacterStats s, AuraStatTotals t) => t.IsEmpty ? s : s with
    {
        MaxHealth = s.MaxHealth == 0 ? 0u : Math.Max(1u, Apply(s.MaxHealth, t, AuraStat.MaxHealth)),
        MaxPower = Apply(s.MaxPower, t, AuraStat.MaxPower),
        Armor = Apply(s.Armor, t, AuraStat.Armor),
        AttackDamage = Apply(s.AttackDamage, t, AuraStat.AttackDamage),
        AbilityDamage = Apply(s.AbilityDamage, t, AuraStat.AbilityDamage),
        CritPct = Apply(s.CritPct, t, AuraStat.CritPct),
        Evasion = Apply(s.Evasion, t, AuraStat.Evasion),
        BlockPct = Apply(s.BlockPct, t, AuraStat.BlockPct),
        MovementSpeedPct = Apply(s.MovementSpeedPct, t, AuraStat.MovementSpeed),
        MaxWard = Apply(s.MaxWard, t, AuraStat.MaxWard),
        Resistances = Apply(s.Resistances, t),
    };

    /// <summary>
    /// A creature's attack with its auras: its damage stats, crit and offence block. Its natural damage range is unchanged.
    /// </summary>
    public static AttackerCombat Fold(in AttackerCombat a, AuraStatTotals t) => t.IsEmpty ? a : a with
    {
        AttackDamage = Apply(a.AttackDamage, t, AuraStat.AttackDamage),
        AbilityDamage = Apply(a.AbilityDamage, t, AuraStat.AbilityDamage),
        CritPct = Apply(a.CritPct, t, AuraStat.CritPct),
        Offence = Fold(a.Offence, t),
    };

    /// <summary>
    /// An offence block with its auras (#1178). On the thirteen Increased… keys, already percentages, a Flat adds its points
    /// to the key's increased sum and a More multiplies the key's more product; an Increased there is refused at load and
    /// counts as nothing. The crit multiplier bonus and the three speeds fold as any percentage stat.
    /// </summary>
    internal static OffenceStats Fold(in OffenceStats o, AuraStatTotals t)
    {
        if (t.IsEmpty) return o;

        OffenceStats folded = o;
        for (AuraStat stat = AuraStat.IncreasedDamage; stat <= AuraStat.IncreasedHealing; stat++)
        {
            var key = (OffenceStat)(stat - AuraStat.IncreasedDamage);
            folded.AddIncreased(key, t.Flat(stat));
            folded.MultiplyMore(key, t.More(stat));
        }

        folded.CritMultiplierBonus = Apply(o.CritMultiplierBonus, t, AuraStat.CritMultiplier);
        folded.AttackSpeedPct = Apply(o.AttackSpeedPct, t, AuraStat.AttackSpeed);
        folded.CastSpeedPct = Apply(o.CastSpeedPct, t, AuraStat.CastSpeed);
        folded.CooldownRecoveryPct = Apply(o.CooldownRecoveryPct, t, AuraStat.CooldownRecovery);
        return folded;
    }

    public static DefenderCombat Fold(in DefenderCombat d, AuraStatTotals t) => t.IsEmpty ? d : d with
    {
        Armor = Apply(d.Armor, t, AuraStat.Armor),
        Evasion = Apply(d.Evasion, t, AuraStat.Evasion),
        BlockPct = Apply(d.BlockPct, t, AuraStat.BlockPct),
        Resistances = Apply(d.Resistances, t),
    };

    /// <summary>Each resistance with its auras (#1178), a percentage stat: a negative flat aura can take it below 0.</summary>
    private static Resistances Apply(in Resistances r, AuraStatTotals t) => new(
        Apply(r.Fire, t, AuraStat.FireResistance),
        Apply(r.Cold, t, AuraStat.ColdResistance),
        Apply(r.Lightning, t, AuraStat.LightningResistance),
        Apply(r.Blight, t, AuraStat.BlightResistance));

    private static double Factor(AuraStatTotals totals, AuraStat stat) =>
        Math.Max(0d, 1d + (totals.Increased(stat) / 100d)) * totals.More(stat);
}
