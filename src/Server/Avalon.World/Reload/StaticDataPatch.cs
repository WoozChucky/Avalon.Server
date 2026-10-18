using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Abilities;
using Avalon.World.Dialogue;
using Avalon.World.Doodads;
using Avalon.World.Loot;
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
    private readonly KeyedRows<ulong, ItemTemplate> _templates = Keyed(Templates);

    /// <summary>
    /// The templates, with their lookup by id (<see cref="KeyedRows{TKey,TRow}" />, #883): one object, replaced with
    /// the patch, so a reload cannot leave a lookup behind.
    /// </summary>
    public IReadOnlyCollection<ItemTemplate> Templates
    {
        get => _templates;
        init => _templates = Keyed(value);
    }

    /// <summary>The template of that id, or null: by key, where a reader walked the templates.</summary>
    public ItemTemplate? Find(ItemTemplateId id) => id is null ? null : _templates.Find(id.Value);

    private static KeyedRows<ulong, ItemTemplate> Keyed(IReadOnlyCollection<ItemTemplate> templates) =>
        KeyedRows<ulong, ItemTemplate>.Over(templates, static template => template.Id?.Value);

    public override string Describe() => $"{Templates.Count} item templates";
}

public sealed record ProgressionPatch(
    IReadOnlyCollection<CharacterLevelExperience> Levels,
    IReadOnlyCollection<ClassLevelStat> ClassStats,
    IReadOnlyCollection<CharacterCreateInfo> CreateInfos)
    : StaticDataPatch(ReloadArea.Progression)
{
    private readonly KeyedRows<ushort, CharacterLevelExperience> _levels = Keyed(Levels);

    /// <summary>The experience rows, with their lookup by level (<see cref="KeyedRows{TKey,TRow}" />, #883), as the item templates have theirs.</summary>
    public IReadOnlyCollection<CharacterLevelExperience> Levels
    {
        get => _levels;
        init => _levels = Keyed(value);
    }

    /// <summary>The experience row of that level, or null: by key, where a reader walked the rows.</summary>
    public CharacterLevelExperience? FindLevel(ushort level) => _levels.Find(level);

    private static KeyedRows<ushort, CharacterLevelExperience> Keyed(IReadOnlyCollection<CharacterLevelExperience> levels) =>
        KeyedRows<ushort, CharacterLevelExperience>.Over(levels, static row => row.Level);

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
        (CombatFormula? f, IReadOnlyDictionary<CharacterClass, ClassStatFactors>? byClass) = CombatDataRules.Build(formulas, factors);
        return new CombatPatch(f, byClass);
    }
}

/// <summary>
/// Quests (#433), validated against the creature and item templates read in the same prepare. Forward-only: a
/// character's log keeps its rows; what changes is what is offered, credited and paid from the next tick.
/// </summary>
public sealed record QuestsPatch(Quests.QuestCatalog Catalog) : StaticDataPatch(ReloadArea.Quests)
{
    public override string Describe() => Catalog.Describe();
}

/// <summary>
/// Aura templates, validated with the aura scripts loaded. Forward-only: an aura already on a unit keeps the template,
/// snapshot and stacks it was applied with; one whose template this patch drops expires on its next tick. An ability's
/// aura link is checked by the Abilities area against the auras its own prepare reads.
/// </summary>
public sealed record AurasPatch(Auras.AuraCatalog Catalog) : StaticDataPatch(ReloadArea.Auras)
{
    public override string Describe() => Catalog.Describe();
}

/// <summary>
/// Doodad templates, validated against the loot tables, auras and quests read in the same prepare and the doodad
/// scripts loaded. Forward-only: a doodad already placed keeps the template it was placed with; what changes is what
/// the next instance build places.
/// </summary>
public sealed record DoodadsPatch(DoodadCatalog Catalog) : StaticDataPatch(ReloadArea.Doodads)
{
    public override string Describe() => Catalog.Describe();
}
