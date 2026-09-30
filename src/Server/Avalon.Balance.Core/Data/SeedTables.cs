using Avalon.Domain.World;

namespace Avalon.Balance.Core;

/// <summary>The seed rows the simulator uses, mutable so overrides can change them before validation.</summary>
public sealed class SeedTables
{
    public required List<AbilityTemplate> AbilityTemplates { get; init; }
    public required List<ClassLevelStat> ClassLevelStats { get; init; }
    public required List<ClassStatFactors> ClassStatFactors { get; init; }
    public required List<CombatFormula> CombatFormulas { get; init; }
    public required List<CreatureBaseStat> CreatureBaseStats { get; init; }
    public required List<CreatureRarityModifier> CreatureRarityModifiers { get; init; }
    public required List<CreatureTemplate> CreatureTemplates { get; init; }
    public required List<ItemTemplate> ItemTemplates { get; init; }
    public required List<CharacterCreateInfo> CharacterCreateInfos { get; init; }
    public required List<CharacterLevelExperience> CharacterLevelExperiences { get; init; }
    public required List<VendorStock> VendorStocks { get; init; }
}
