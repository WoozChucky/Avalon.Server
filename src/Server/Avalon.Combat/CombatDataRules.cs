using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// The checks on the combat formula (#506), shared by the world server's Combat reload and the balance simulator.
/// Refuses the row, naming it.
/// </summary>
public static class CombatDataRules
{
    /// <exception cref="InvalidDataException">A row is missing or out of range; the message names it.</exception>
    public static CombatFormula Build(IReadOnlyCollection<CombatFormula> formulas)
    {
        if (formulas.Count != 1 || formulas.Single().Id != CombatFormula.SingletonId)
        {
            throw new InvalidDataException(
                $"CombatFormula must hold exactly one row, id {CombatFormula.SingletonId}; it holds {formulas.Count}");
        }

        CombatFormula f = formulas.Single();
        RequireNonNegative("CombatFormula 1", nameof(f.ArmorBase), f.ArmorBase);
        RequireNonNegative("CombatFormula 1", nameof(f.ArmorPerLevel), f.ArmorPerLevel);
        RequireNonNegative("CombatFormula 1", nameof(f.CritMultiplier), f.CritMultiplier);
        RequireNonNegative("CombatFormula 1", nameof(f.BlockMultiplier), f.BlockMultiplier);
        RequireRange("CombatFormula 1", nameof(f.ArmorCap), f.ArmorCap, 0f, 1f);
        RequireRange("CombatFormula 1", nameof(f.CritCap), f.CritCap, 0f, 100f);
        RequireRange("CombatFormula 1", nameof(f.BlockCap), f.BlockCap, 0f, 100f);
        if (!(f.ArmorBase + f.ArmorPerLevel > 0f))
            throw new InvalidDataException("CombatFormula 1: ArmorBase + ArmorPerLevel must be above 0");

        // #627: a movement speed at or below -100 % would stop a character dead, or reverse it.
        if (!float.IsFinite(f.MoveSpeedCap) || !float.IsFinite(f.MoveSpeedFloor)
            || !(f.MoveSpeedFloor > -100f) || !(f.MoveSpeedFloor <= f.MoveSpeedCap))
        {
            throw new InvalidDataException(
                $"CombatFormula 1: MoveSpeedFloor and MoveSpeedCap must be finite, with -100 < floor <= cap, not {f.MoveSpeedFloor} and {f.MoveSpeedCap}");
        }

        // #1178: the evasion terms divide a rating as the armour terms do; a resistance stays under the hard ceiling of
        // 90, and the floor lets a negative one at most double an element. The attribute conversions are class-neutral.
        RequireNonNegative("CombatFormula 1", nameof(f.EvasionBase), f.EvasionBase);
        RequireNonNegative("CombatFormula 1", nameof(f.EvasionPerLevel), f.EvasionPerLevel);
        if (!(f.EvasionBase + f.EvasionPerLevel > 0f))
            throw new InvalidDataException("CombatFormula 1: EvasionBase + EvasionPerLevel must be above 0");
        RequireRange("CombatFormula 1", nameof(f.EvasionCap), f.EvasionCap, 0f, 100f);
        RequireRange("CombatFormula 1", nameof(f.ResistanceCap), f.ResistanceCap, 0f, 90f);
        RequireRange("CombatFormula 1", nameof(f.ResistanceFloor), f.ResistanceFloor, -100f, 0f);
        RequireNonNegative("CombatFormula 1", nameof(f.ArmorPerStrength), f.ArmorPerStrength);
        RequireNonNegative("CombatFormula 1", nameof(f.EvasionPerAgility), f.EvasionPerAgility);
        RequireNonNegative("CombatFormula 1", nameof(f.WardPerIntellect), f.WardPerIntellect);

        // #1178, offence: the attributes' conversions and the speed caps are finite and 0 or more, the base crit chance
        // percentage points, and the bare hand rolls an ordered range at a pace above 0.
        RequireNonNegative("CombatFormula 1", nameof(f.IncreasedPhysicalAttackPerStrength), f.IncreasedPhysicalAttackPerStrength);
        RequireNonNegative("CombatFormula 1", nameof(f.AttackSpeedPerAgility), f.AttackSpeedPerAgility);
        RequireNonNegative("CombatFormula 1", nameof(f.IncreasedSpellPerIntellect), f.IncreasedSpellPerIntellect);
        RequireNonNegative("CombatFormula 1", nameof(f.IncreasedHealingPerIntellect), f.IncreasedHealingPerIntellect);
        RequireNonNegative("CombatFormula 1", nameof(f.AttackSpeedCap), f.AttackSpeedCap);
        RequireNonNegative("CombatFormula 1", nameof(f.CastSpeedCap), f.CastSpeedCap);
        RequireNonNegative("CombatFormula 1", nameof(f.CooldownRecoveryCap), f.CooldownRecoveryCap);
        RequireRange("CombatFormula 1", nameof(f.BaseCritPct), f.BaseCritPct, 0f, 100f);
        if (f.UnarmedDamageMin > f.UnarmedDamageMax)
        {
            throw new InvalidDataException(
                $"CombatFormula 1: UnarmedDamageMin {f.UnarmedDamageMin} is above UnarmedDamageMax {f.UnarmedDamageMax}");
        }

        if (f.UnarmedAttackTimeMs == 0)
            throw new InvalidDataException("CombatFormula 1: UnarmedAttackTimeMs must be above 0");

        // #1181: the mana formula's Intellect conversion; its whole-number terms are uints.
        RequireNonNegative("CombatFormula 1", nameof(f.ManaPerIntellect), f.ManaPerIntellect);

        return f;
    }

    private static void RequireNonNegative(string row, string column, double value)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new InvalidDataException($"{row}: {column} must be finite and 0 or more, not {value}");
    }

    private static void RequireRange(string row, string column, float value, float min, float max)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"{row}: {column} must be between {min} and {max}, not {value}");
    }
}
