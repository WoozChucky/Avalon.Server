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
            CombatFormulas = reader.Rows<CombatFormula>(),
            CreatureBaseStats = reader.Rows<CreatureBaseStat>(),
            CreatureRarityModifiers = reader.Rows<CreatureRarityModifier>(),
            CreatureTemplates = reader.Rows<CreatureTemplate>(),
            ItemTemplates = reader.Rows<ItemTemplate>(),
            CharacterLevelExperiences = reader.Rows<CharacterLevelExperience>(),
            VendorStocks = reader.Rows<VendorStock>(),
            AuraTemplates = reader.Rows<AuraTemplate>(),
            AuraStatModifiers = reader.Rows<AuraStatModifier>(),
            TalentNodes = reader.Rows<TalentNode>(),
            TalentEdges = reader.Rows<TalentEdge>(),
            TalentNodeStats = reader.Rows<TalentNodeStat>(),
            TalentAbilityModifiers = reader.Rows<TalentAbilityModifier>(),
            TalentNodeAbilities = reader.Rows<TalentNodeAbility>(),
            TalentClassStarts = reader.Rows<TalentClassStart>(),
            ItemTemplateBaseStats = reader.Rows<ItemTemplateBaseStat>(),
            ItemModifierGroups = reader.Rows<ItemModifierGroup>(),
            ItemModifiers = reader.Rows<ItemModifier>(),
            ItemModifierStats = reader.Rows<ItemModifierStat>(),
            ItemTemplateModifiers = reader.Rows<ItemTemplateModifier>(),
        };
    }
}
