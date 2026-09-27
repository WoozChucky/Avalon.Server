using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Combat;

/// <summary>
/// What the attacking side of a hit brings (#506). Chances are percentage points. A creature has no
/// weapon (0 and 0) and no damage stats, since it has no abilities yet.
/// </summary>
public readonly record struct AttackerCombat(
    uint Level, uint AttackDamage, uint AbilityDamage, float CritPct, uint WeaponMin, uint WeaponMax);

/// <summary>What the defending side of a hit brings (#506). Chances are percentage points.</summary>
public readonly record struct DefenderCombat(uint Armor, float DodgePct, float BlockPct);

/// <summary>
/// The hit maths (#506). Pure: the numbers come from <see cref="ICombatRandom" />, the constants from the
/// <see cref="CombatFormula" /> the caller read once for this hit.
/// </summary>
/// <remarks>
/// A damage hit draws, in this order: a dodge roll; then, when not dodged, a crit roll and a block roll.
/// The order of the draws is part of the contract. The steps are: base, dodge (0, and nothing else is
/// drawn), crit (× CritMultiplier), block (× BlockMultiplier), armour
/// (× (1 − min(A / (A + ArmorBase + ArmorPerLevel × level), ArmorCap))), then floor with a minimum of 1.
/// Each chance is clamped to its cap, and one that is not a finite value of 0 or more counts as 0.
/// </remarks>
public static class HitResolver
{
    /// <summary>
    /// An ability's base: <c>effectValue + scaling × stat + weaponCoefficient × roll</c>, the stat being
    /// AttackDamage or AbilityDamage. The weapon is rolled, uniform and inclusive over the main hand's
    /// range, only when the coefficient is above 0 and a weapon is worn.
    /// </summary>
    public static float AbilityBase(in AttackerCombat a, float effectValue, ScalingStat stat, float scaling,
        float weaponCoefficient, ICombatRandom rng)
    {
        double statValue = stat == ScalingStat.Ability ? a.AbilityDamage : a.AttackDamage;
        double total = NonNegative(effectValue) + NonNegative(scaling) * statValue;

        if (NonNegative(weaponCoefficient) > 0 && a.WeaponMax > 0)
        {
            long min = Math.Min(a.WeaponMin, a.WeaponMax);
            total += weaponCoefficient * rng.NextInt64(min, a.WeaponMax);
        }

        return (float)total;
    }

    public static (uint Damage, HitResult Result) ResolveDamage(in AttackerCombat a, in DefenderCombat d,
        float baseDamage, CombatFormula f, ICombatRandom rng)
    {
        if (rng.NextDouble() < Chance(d.DodgePct, f.DodgeCap))
            return (0u, HitResult.Dodged);

        double damage = NonNegative(baseDamage);
        HitResult result = HitResult.None;

        if (rng.NextDouble() < Chance(a.CritPct, f.CritCap))
        {
            damage *= NonNegative(f.CritMultiplier);
            result |= HitResult.Crit;
        }

        if (rng.NextDouble() < Chance(d.BlockPct, f.BlockCap))
        {
            damage *= NonNegative(f.BlockMultiplier);
            result |= HitResult.Blocked;
        }

        damage *= 1d - ArmorReduction(d.Armor, a.Level, f);

        return (Math.Max(1u, Floor(damage)), result);
    }

    /// <summary>
    /// A heal: the base, then a crit roll (the one draw). Never dodged, blocked or reduced by armour, and
    /// floored with no minimum, so a heal of nothing stays nothing.
    /// </summary>
    public static (uint Heal, HitResult Result) ResolveHeal(in AttackerCombat a, float baseHeal, CombatFormula f,
        ICombatRandom rng)
    {
        double heal = NonNegative(baseHeal);
        HitResult result = HitResult.None;

        if (rng.NextDouble() < Chance(a.CritPct, f.CritCap))
        {
            heal *= NonNegative(f.CritMultiplier);
            result |= HitResult.Crit;
        }

        return (Floor(heal), result);
    }

    /// <summary>
    /// The share of a hit armour takes, 0 with no armour, never above ArmorCap. The load refuses a formula
    /// whose armour terms are not above 0 together, so the denominator is above 0 for any armour.
    /// </summary>
    public static double ArmorReduction(uint armor, uint attackerLevel, CombatFormula f)
    {
        if (armor == 0) return 0d;

        double denominator = armor + (double)f.ArmorBase + (double)f.ArmorPerLevel * attackerLevel;
        double cap = Math.Clamp(NonNegative(f.ArmorCap), 0d, 1d);
        // Unreachable with a formula the load accepted; the cap is the most armour can ever take.
        if (!(denominator > 0)) return cap;

        return Math.Clamp(armor / denominator, 0d, cap);
    }

    /// <summary>A chance in [0, 1): the points clamped to the cap, a bad value counting as 0.</summary>
    private static double Chance(float pct, float cap) =>
        Math.Min(NonNegative(pct), NonNegative(cap)) / 100d;

    private static double NonNegative(double value) => double.IsFinite(value) && value > 0 ? value : 0d;

    private static uint Floor(double value) => value >= uint.MaxValue ? uint.MaxValue : (uint)Math.Floor(value);
}
