using Avalon.Balance.Core;
using Avalon.Domain.World;

namespace Avalon.Balance.Data;

public static class SeedSource
{
    public static SeedTables Load()
    {
        using var reader = new SeedReader();
        return new SeedTables
        {
            AbilityTemplates = reader.Rows<AbilityTemplate>(),
            ClassLevelStats = reader.Rows<ClassLevelStat>(),
            ClassStatFactors = reader.Rows<ClassStatFactors>(),
            CombatFormulas = reader.Rows<CombatFormula>(),
            CreatureBaseStats = reader.Rows<CreatureBaseStat>(),
            CreatureRarityModifiers = reader.Rows<CreatureRarityModifier>(),
            CreatureTemplates = reader.Rows<CreatureTemplate>(),
            ItemTemplates = reader.Rows<ItemTemplate>(),
            CharacterCreateInfos = reader.Rows<CharacterCreateInfo>(),
            CharacterLevelExperiences = reader.Rows<CharacterLevelExperience>(),
            VendorStocks = reader.Rows<VendorStock>(),
            AuraTemplates = reader.Rows<AuraTemplate>(),
            AuraStatModifiers = reader.Rows<AuraStatModifier>(),
        };
    }
}
