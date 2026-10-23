using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>
/// What the attacking side of a hit brings (#506, #1178). CritPct is percentage points. Weapon is what an Attack rolls: a
/// character's main hand (the bare hand's range with none), a creature's natural DamageMin..DamageMax (#163). Offence is
/// the attacker's offence block, read by <see cref="OffenceMath" /> and the crit multiplier. BaseType is the type of a hit
/// whose ability names none: a creature's template's, a character's Physical. Penetration has no data source yet.
/// </summary>
public readonly record struct AttackerCombat(uint Level, float CritPct, WeaponDamage Weapon = default,
    OffenceStats Offence = default, DamageType BaseType = DamageType.Physical, Penetration Penetration = default);

/// <summary>
/// What the defending side of a hit brings (#506, #1178). Evasion is a rating, weighed against the attacker's level;
/// BlockPct is percentage points; resistances are percent. MaxResistanceBonus raises each element's cap and has no data
/// source yet.
/// </summary>
public readonly record struct DefenderCombat(uint Armor, uint Evasion, float BlockPct,
    Resistances Resistances = default, Resistances MaxResistanceBonus = default);

/// <summary>
/// The hit maths (#506, #1178). Pure: the numbers come from <see cref="ICombatRandom" />, the constants from the
/// <see cref="CombatFormula" /> the caller read once for this hit.
/// </summary>
/// <remarks>
/// A damage hit draws, in this order: an evasion roll (none for an Unavoidable ability, #1164); then, when not evaded,
/// a crit roll and a block roll. The order of the draws is part of the contract. The steps are: base, evasion (0, and
/// nothing else is drawn), crit (× <see cref="CritMultiplier" />), block (× BlockMultiplier), then each damage type's
/// part on its own × its mitigation (<see cref="Mitigation" />: armour for physical, resistance for an element), then the
/// sum floored with a minimum of 1. Each chance is clamped to its cap, and one that is not a finite value of 0 or more counts as 0.
/// </remarks>
public static class HitResolver
{
    /// <summary>The most any element can ever be resisted, whatever its cap and bonus (#1178), so none is ever immune.</summary>
    public const float ResistanceCeiling = 90f;

    /// <summary>The type of a hit (#1178): the ability's, else the attacker's base type.</summary>
    public static DamageType TypeOf(DamageType? abilityType, in AttackerCombat attacker) =>
        abilityType ?? attacker.BaseType;

    /// <summary>
    /// What <see cref="ResolveDamage" /> deals from <paramref name="baseDamage" /> when nothing procs and the
    /// defender has no armour (#669): the base, floored, with a minimum of 1.
    /// </summary>
    public static uint NormalDamage(double baseDamage) => AbilityAmountMath.NormalDamage(baseDamage);

    /// <summary>What <see cref="ResolveHeal" /> restores from <paramref name="baseHeal" /> without a crit (#669): the base, floored.</summary>
    public static uint NormalHeal(double baseHeal) => AbilityAmountMath.NormalHeal(baseHeal);

    /// <summary>
    /// The chance to evade an attacker of <paramref name="attackerLevel" /> (#1178), armour's shape:
    /// <c>min(E / (E + EvasionBase + EvasionPerLevel × level), EvasionCap / 100)</c>, in [0, EvasionCap / 100]; 0 with
    /// no evasion. The load refuses a formula whose evasion terms are not above 0 together.
    /// </summary>
    public static double EvasionChance(uint evasion, uint attackerLevel, CombatFormula f)
    {
        if (evasion == 0) return 0d;

        double cap = Math.Clamp(NonNegative(f.EvasionCap), 0d, 100d) / 100d;
        double denominator = evasion + (double)f.EvasionBase + (double)f.EvasionPerLevel * attackerLevel;
        // Unreachable with a formula the load accepted; the cap is the most evasion can ever give.
        if (!(denominator > 0)) return cap;

        return Math.Clamp(evasion / denominator, 0d, cap);
    }

    /// <summary>
    /// Whether a Hostile ability is evaded by the unit it reaches (#1164, #1178): one draw against the defender's chance
    /// to evade an attacker of <paramref name="attackerLevel" />. A damage hit makes it first (<see cref="ResolveDamage" />);
    /// an ability that deals no damage makes it alone, and its aura lands only when it is not evaded.
    /// </summary>
    public static bool Evades(in DefenderCombat d, uint attackerLevel, CombatFormula f, ICombatRandom rng) =>
        rng.NextDouble() < EvasionChance(d.Evasion, attackerLevel, f);

    /// <summary>
    /// The resistance that counts before penetration (#1178), in percent: <paramref name="resistance" /> at most
    /// <c>ResistanceCap + maxBonus</c> and never above <see cref="ResistanceCeiling" />, nor below ResistanceFloor. The
    /// character sheet shows it. A value that is not finite counts as 0.
    /// </summary>
    public static float EffectiveResistance(float resistance, float maxBonus, CombatFormula f)
    {
        double cap = Math.Min(Finite(f.ResistanceCap) + Finite(maxBonus), ResistanceCeiling);
        return (float)Math.Clamp(Math.Min(Finite(resistance), cap), ResistanceFloor(f), ResistanceCeiling);
    }

    /// <summary>
    /// What is left of an element after its resistance (#1178): <c>1 − (effective − penetration) / 100</c>, the difference
    /// held to [ResistanceFloor, <see cref="ResistanceCeiling" />], so penetration comes off after the cap.
    /// </summary>
    public static double ResistanceMultiplier(float resistance, float maxBonus, float penetration, CombatFormula f)
    {
        double effective = Math.Clamp((double)EffectiveResistance(resistance, maxBonus, f) - Finite(penetration),
            ResistanceFloor(f), ResistanceCeiling);
        return 1d - effective / 100d;
    }

    /// <summary>
    /// What is left of a part of <paramref name="type" /> after the defender's mitigation (#1178): physical meets armour
    /// (<c>1 − ArmorReduction</c>), an element its resistance less the attacker's penetration.
    /// </summary>
    public static double Mitigation(DamageType type, in AttackerCombat a, in DefenderCombat d, CombatFormula f) =>
        type == DamageType.Physical
            ? 1d - ArmorReduction(d.Armor, a.Level, f)
            : ResistanceMultiplier(d.Resistances.Of(type), d.MaxResistanceBonus.Of(type), a.Penetration.Of(type), f);

    /// <summary>
    /// A damage hit (#1178): <paramref name="baseDamage" /> split by type, each part mitigated on its own after the
    /// evasion, crit and block draws (in that order). An evaded hit deals 0 and draws nothing after the evasion roll.
    /// </summary>
    /// <param name="unavoidable">
    /// An Unavoidable ability's hit (#1164): no evasion roll is drawn, and the hit goes straight to its crit roll.
    /// </param>
    public static HitDamage ResolveDamage(in AttackerCombat a, in DefenderCombat d, in DamageParts baseDamage,
        CombatFormula f, ICombatRandom rng, bool unavoidable = false)
    {
        DamageType fallback = baseDamage.Primary(a.BaseType);
        DamageTypeMask types = baseDamage.Mask;

        if (!unavoidable && Evades(d, a.Level, f, rng))
            return new HitDamage(0u, HitResult.Dodged, fallback, types, 0u);

        double scale = 1d;
        HitResult result = HitResult.None;

        if (rng.NextDouble() < Chance(a.CritPct, f.CritCap))
        {
            scale *= CritMultiplier(a, f);
            result |= HitResult.Crit;
        }

        if (rng.NextDouble() < Chance(d.BlockPct, f.BlockCap))
        {
            scale *= NonNegative(f.BlockMultiplier);
            result |= HitResult.Blocked;
        }

        // One pass: the sum, the largest mitigated part (a tie going to the lower type) and the blight part.
        double sum = 0d;
        double largest = 0d;
        double blight = 0d;
        DamageType primary = fallback;
        for (int i = 0; i < DamageTypes.Count; i++)
        {
            var type = (DamageType)i;
            double part = NonNegative(baseDamage.Get(type));
            if (part == 0d) continue;

            double dealt = part * scale * Mitigation(type, a, d, f);
            sum += dealt;
            if (dealt > largest)
            {
                largest = dealt;
                primary = type;
            }

            if (type == DamageType.Blight) blight = dealt;
        }

        uint total = Math.Max(1u, Floor(sum));
        return new HitDamage(total, result, primary, types, Absorbable(total, blight, sum));
    }

    /// <summary>
    /// The share of a hit's <paramref name="total" /> a ward may take (#1178): all of it but its blight, the blight's
    /// share of the mitigated <paramref name="sum" /> rounded half away from zero. All of it with no blight, none when
    /// only blight.
    /// </summary>
    private static uint Absorbable(uint total, double blight, double sum)
    {
        if (!(blight > 0d)) return total;
        if (!(sum > blight)) return 0u;

        double blightShare = Math.Round(total * blight / sum, MidpointRounding.AwayFromZero);
        return blightShare >= total ? 0u : total - (uint)blightShare;
    }

    /// <summary>
    /// Whether a ward may absorb damage of <paramref name="type" /> (#1178): everything but blight, and but physical
    /// damage over time (a bleed).
    /// </summary>
    public static bool WardAbsorbs(DamageType type, bool periodic) =>
        type != DamageType.Blight && !(periodic && type == DamageType.Physical);

    /// <summary>
    /// A heal: the base, then a crit roll (the one draw). Never dodged, blocked or reduced by armour, and
    /// floored with no minimum, so a heal of nothing stays nothing.
    /// </summary>
    public static (uint Heal, HitResult Result) ResolveHeal(in AttackerCombat a, double baseHeal, CombatFormula f,
        ICombatRandom rng)
    {
        (double heal, HitResult result) = ResolvePeriodicHeal(a, baseHeal, f, rng);
        return (Floor(heal), result);
    }

    /// <summary>
    /// An aura's damage tick: the base, then a crit roll (the one draw), then the mitigation of its
    /// <paramref name="type" /> (#1178: armour for physical, resistance for an element); never evaded or blocked.
    /// <paramref name="a" /> is the caster as the aura snapshotted it. Not floored and with no minimum: the aura carries
    /// the fraction from tick to tick (<see cref="AuraRules.TakeTick" />), so its ticks add up to its total.
    /// </summary>
    public static (double Damage, HitResult Result) ResolvePeriodic(in AttackerCombat a, in DefenderCombat d,
        DamageType type, double baseDamage, CombatFormula f, ICombatRandom rng)
    {
        double damage = NonNegative(baseDamage);
        HitResult result = HitResult.None;

        if (rng.NextDouble() < Chance(a.CritPct, f.CritCap))
        {
            damage *= CritMultiplier(a, f);
            result |= HitResult.Crit;
        }

        damage *= Mitigation(type, a, d, f);

        return (damage, result);
    }

    /// <summary>
    /// A heal before flooring: the base, then a crit roll (the one draw). An aura's heal tick takes it as it is and
    /// carries the fraction (<see cref="AuraRules.TakeTick" />); <see cref="ResolveHeal" /> floors it.
    /// </summary>
    public static (double Heal, HitResult Result) ResolvePeriodicHeal(in AttackerCombat a, double baseHeal,
        CombatFormula f, ICombatRandom rng)
    {
        double heal = NonNegative(baseHeal);
        HitResult result = HitResult.None;

        if (rng.NextDouble() < Chance(a.CritPct, f.CritCap))
        {
            heal *= CritMultiplier(a, f);
            result |= HitResult.Crit;
        }

        return (heal, result);
    }

    /// <summary>
    /// What a crit multiplies by (#1178): the formula's <c>CritMultiplier</c> plus the attacker's crit multiplier bonus,
    /// in points, as <c>max(0, CritMultiplier + bonus / 100)</c>. A value that is not finite counts as 0.
    /// </summary>
    public static double CritMultiplier(in AttackerCombat a, CombatFormula f) =>
        NonNegative(Finite(f.CritMultiplier) + (Finite(a.Offence.CritMultiplierBonus) / 100d));

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
    private static double Chance(float pct, float cap) => EffectivePct(pct, cap) / 100d;

    /// <summary>
    /// The chance a roll is made against, in percentage points: <paramref name="pct" /> clamped to
    /// <paramref name="cap" />, a bad value counting as 0. The character sheet shows this (#506).
    /// </summary>
    public static float EffectivePct(float pct, float cap) => (float)Math.Min(NonNegative(pct), NonNegative(cap));

    private static double NonNegative(double value) => double.IsFinite(value) && value > 0 ? value : 0d;

    private static double Finite(double value) => double.IsFinite(value) ? value : 0d;

    /// <summary>The formula's floor, never above 0, so it always sits below the ceiling.</summary>
    private static double ResistanceFloor(CombatFormula f) => Math.Min(Finite(f.ResistanceFloor), 0d);

    private static uint Floor(double value) => value >= uint.MaxValue ? uint.MaxValue : (uint)Math.Floor(value);
}
