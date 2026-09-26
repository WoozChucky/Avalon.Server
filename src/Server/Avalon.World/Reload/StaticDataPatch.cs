using Avalon.Domain.World;
using Avalon.World.Creatures;
using Avalon.World.Dialogue;
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
}

public sealed record AbilitiesPatch(IReadOnlyCollection<AbilityTemplate> Templates)
    : StaticDataPatch(ReloadArea.Abilities)
{
    public override string Describe() => $"{Templates.Count} ability templates";
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
