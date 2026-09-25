using Avalon.Domain.World;
using Avalon.World.Creatures;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Localization;

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
    ILocalizedTextCatalog Texts, IDialogueCatalog Dialogue, int TextCount, int NodeCount, int OptionCount)
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
