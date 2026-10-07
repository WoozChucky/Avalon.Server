using Avalon.Domain.World;
using Avalon.World.Public.Enums;

namespace Avalon.Combat;

/// <summary>
/// The checks on the combat formula and every class's stat factors (#506), shared by the world server's
/// Combat reload and the balance simulator. Refuses the whole set, naming the row.
/// </summary>
public static class CombatDataRules
{
    /// <exception cref="InvalidDataException">A row is missing or out of range; the message names it.</exception>
    public static (CombatFormula Formula, IReadOnlyDictionary<CharacterClass, ClassStatFactors> Factors) Build(
        IReadOnlyCollection<CombatFormula> formulas, IReadOnlyCollection<ClassStatFactors> factors)
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
        RequireRange("CombatFormula 1", nameof(f.ArmorCap), f.ArmorCap, 1f);
        RequireRange("CombatFormula 1", nameof(f.CritCap), f.CritCap, 100f);
        RequireRange("CombatFormula 1", nameof(f.DodgeCap), f.DodgeCap, 100f);
        RequireRange("CombatFormula 1", nameof(f.BlockCap), f.BlockCap, 100f);
        if (!(f.ArmorBase + f.ArmorPerLevel > 0f))
            throw new InvalidDataException("CombatFormula 1: ArmorBase + ArmorPerLevel must be above 0");

        // #627: a speed at or below -100 % would stop a character dead, or reverse it.
        RequireNonNegative("CombatFormula 1", nameof(f.HasteCap), f.HasteCap);
        if (!float.IsFinite(f.MoveSpeedCap) || !float.IsFinite(f.MoveSpeedFloor)
            || !(f.MoveSpeedFloor > -100f) || !(f.MoveSpeedFloor <= f.MoveSpeedCap))
        {
            throw new InvalidDataException(
                $"CombatFormula 1: MoveSpeedFloor and MoveSpeedCap must be finite, with -100 < floor <= cap, not {f.MoveSpeedFloor} and {f.MoveSpeedCap}");
        }

        var byClass = new Dictionary<CharacterClass, ClassStatFactors>();
        foreach (ClassStatFactors row in factors)
        {
            string name = $"ClassStatFactors {row.Class}";
            if (!Enum.IsDefined(row.Class))
                throw new InvalidDataException($"{name}: not a class");
            if (!byClass.TryAdd(row.Class, row))
                throw new InvalidDataException($"{name}: the class has two rows");

            RequireUInt(name, nameof(row.HpPerStamina), row.HpPerStamina);
            if (row.FixedPower is { } fixedPower)
                RequireUInt(name, nameof(row.FixedPower), fixedPower);
            RequireNonNegative(name, nameof(row.PowerPerIntellect), row.PowerPerIntellect);
            RequireNonNegative(name, nameof(row.PowerPerAgility), row.PowerPerAgility);
            RequireNonNegative(name, nameof(row.AttackPerStrength), row.AttackPerStrength);
            RequireNonNegative(name, nameof(row.AttackPerAgility), row.AttackPerAgility);
            RequireNonNegative(name, nameof(row.AbilityPerIntellect), row.AbilityPerIntellect);
            RequireNonNegative(name, nameof(row.BaseBlock), row.BaseBlock);
            RequireNonNegative(name, nameof(row.BaseDodge), row.BaseDodge);
            RequireNonNegative(name, nameof(row.BaseCrit), row.BaseCrit);
        }

        foreach (CharacterClass @class in Enum.GetValues<CharacterClass>())
        {
            if (!byClass.ContainsKey(@class))
                throw new InvalidDataException($"ClassStatFactors {@class}: the class has no row");
        }

        return (f, byClass);
    }

    private static void RequireNonNegative(string row, string column, double value)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new InvalidDataException($"{row}: {column} must be finite and 0 or more, not {value}");
    }

    private static void RequireUInt(string row, string column, long value)
    {
        if (value is < 0 or > uint.MaxValue)
            throw new InvalidDataException($"{row}: {column} must be between 0 and {uint.MaxValue}, not {value}");
    }

    private static void RequireRange(string row, string column, float value, float max)
    {
        if (!float.IsFinite(value) || value < 0 || value > max)
            throw new InvalidDataException($"{row}: {column} must be between 0 and {max}, not {value}");
    }
}
