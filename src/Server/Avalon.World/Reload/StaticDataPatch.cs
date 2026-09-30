using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.World.Abilities;
using Avalon.World.Dialogue;
using Avalon.World.Loot;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Enums;
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
    public override string Describe()
        => $"{Templates.Count} templates, {BaseStats.Count} base stats, {Rarities.Count} rarities";

    /// <summary>#627: see <see cref="CreatureTemplateRules.Validate" />.</summary>
    public static void Validate(IEnumerable<CreatureTemplate> templates) => CreatureTemplateRules.Validate(templates);
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
        var (f, byClass) = CombatDataRules.Build(formulas, factors);
        return new CombatPatch(f, byClass);
    }
}
