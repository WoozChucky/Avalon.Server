using Avalon.Domain.World;
using Avalon.World.Abilities;
using Avalon.World.Creatures;
using Avalon.World.Dialogue;
using Avalon.World.Loot;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Localization;
using Avalon.World.Vendors;

namespace Avalon.World.Reload;

/// <summary>
/// Everything one area assigns, built entirely before anything is applied. An area is never
/// half-reloaded: prepare either produces a whole patch or throws.
/// </summary>
public abstract record StaticDataPatch(ReloadArea Area)
{
    /// <summary>What changed, for the game master's reply — e.g. "14 texts, 6 nodes, 9 options".</summary>
    public abstract string Describe();
}

public sealed record DialoguePatch(
    ILocalizedTextCatalog Texts,
    IDialogueCatalog Dialogue,
    int TextCount,
    int NodeCount,
    int OptionCount,
    DialogueActions Actions)
    : StaticDataPatch(ReloadArea.Dialogue)
{
    public override string Describe() => $"{TextCount} texts, {NodeCount} nodes, {OptionCount} options";
}

public sealed record CreaturesPatch(
    IReadOnlyCollection<CreatureTemplate> Templates,
    IReadOnlyCollection<CreatureBaseStat> BaseStats,
    IReadOnlyCollection<CreatureRarityModifier> Rarities,
    CreatureStatDeriver Stats)
    : StaticDataPatch(ReloadArea.Creatures)
{
    /// <summary>The shortest swing interval a template may have, in seconds (#627); a database check says the same.</summary>
    public const float MinBaseAttackTime = 0.5f;

    public override string Describe()
        => $"{Templates.Count} templates, {BaseStats.Count} base stats, {Rarities.Count} rarities";

    /// <summary>
    /// #627: refuses the whole area, naming the template, when one swings more often than every half second
    /// or at an interval that is not finite, so a bad row leaves the previous generation live.
    /// </summary>
    /// <exception cref="InvalidDataException">A template is out of range; the message names it.</exception>
    public static void Validate(IEnumerable<CreatureTemplate> templates)
    {
        foreach (CreatureTemplate t in templates)
        {
            if (!float.IsFinite(t.BaseAttackTime) || t.BaseAttackTime < MinBaseAttackTime)
                throw new InvalidDataException(
                    $"CreatureTemplate {t.Id.Value}: BaseAttackTime must be finite and {MinBaseAttackTime} s or more, not {t.BaseAttackTime}");
        }
    }
}

/// <summary>
/// Ability templates, validated (#164). Read when a character is selected, so a reload reaches the
/// next select; abilities a character already holds keep the metadata they were built with.
/// </summary>
public sealed record AbilitiesPatch(AbilityCatalog Catalog) : StaticDataPatch(ReloadArea.Abilities)
{
    public override string Describe() => Catalog.Describe();
}

public sealed record ItemsPatch(IReadOnlyCollection<ItemTemplate> Templates)
    : StaticDataPatch(ReloadArea.Items)
{
    public override string Describe() => $"{Templates.Count} item templates";
}

public sealed record ProgressionPatch(
    IReadOnlyCollection<CharacterLevelExperience> Levels,
    IReadOnlyCollection<ClassLevelStat> ClassStats,
    IReadOnlyCollection<CharacterCreateInfo> CreateInfos)
    : StaticDataPatch(ReloadArea.Progression)
{
    public override string Describe()
        => $"{Levels.Count} levels, {ClassStats.Count} class stats, {CreateInfos.Count} create infos";
}

/// <summary>
/// Loot tables. Rolled when a creature dies, so a reload changes the next kill; drops already on
/// the ground keep what they rolled.
/// </summary>
public sealed record LootPatch(LootCatalog Catalog) : StaticDataPatch(ReloadArea.Loot)
{
    public override string Describe() => Catalog.Describe();
}

/// <summary>
/// Vendor stock (#432). Read whenever a shop lists or sells, so a reload changes the next list. On
/// its next vendor pass each town instance carries its live stock counts over by row id, and
/// every open shop hears the new list.
/// </summary>
public sealed record VendorsPatch(VendorCatalog Catalog) : StaticDataPatch(ReloadArea.Vendors)
{
    public override string Describe() => Catalog.Describe();
}

/// <summary>
/// The combat formula and every class's stat factors (#506), validated together. Forward-only: the next
/// hit resolves with the new formula, and a character's derived stats change at its next stats refresh
/// (select, gear change, level-up). Build refuses the whole area, naming the row, so a bad row leaves the
/// previous generation live.
/// </summary>
public sealed record CombatPatch(CombatFormula Formula, IReadOnlyDictionary<CharacterClass, ClassStatFactors> Factors)
    : StaticDataPatch(ReloadArea.Combat)
{
    public override string Describe() => $"1 formula, {Factors.Count} class stat factors";

    /// <exception cref="InvalidDataException">A row is missing or out of range; the message names it.</exception>
    public static CombatPatch Build(IReadOnlyCollection<CombatFormula> formulas, IReadOnlyCollection<ClassStatFactors> factors)
    {
        if (formulas.Count != 1 || formulas.Single().Id != CombatFormula.SingletonId)
            throw new InvalidDataException(
                $"CombatFormula must hold exactly one row, id {CombatFormula.SingletonId}; it holds {formulas.Count}");

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
            throw new InvalidDataException(
                $"CombatFormula 1: MoveSpeedFloor and MoveSpeedCap must be finite, with -100 < floor <= cap, not {f.MoveSpeedFloor} and {f.MoveSpeedCap}");

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

        return new CombatPatch(f, byClass);
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

