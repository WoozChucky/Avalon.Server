using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Seeding;
using Avalon.World;
using Avalon.World.Testing.Scenarios;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// An item's name as it is described (#1179), over the seeded catalog and templates: a magic item from its prefix
/// and suffix around the template's name, a rare by the two words its roll stored, and common and unique items (null) by
/// the template's own. An implicit never names an item.
/// </summary>
public class ItemNamesShould
{
    private static readonly StaticData s_data = ScenarioReferenceData.Shared.Data;

    [Theory]
    [InlineData(32UL, ItemRarity.Common, new string[0], null, null)]
    [InlineData(32UL, ItemRarity.Magic, new[] { "P2 T2", "S6 T3" }, null, "Smouldering Ironwood Sword of Haste")]
    [InlineData(32UL, ItemRarity.Magic, new[] { "P2 T2" }, null, "Smouldering Ironwood Sword")]
    [InlineData(32UL, ItemRarity.Magic, new[] { "S6 T3" }, null, "Ironwood Sword of Haste")]
    [InlineData(32UL, ItemRarity.Magic, new[] { "P8 Fire T1" }, null, "Ashen Ironwood Sword")]
    [InlineData(32UL, ItemRarity.Rare, new[] { "P1 T1", "P2 T2", "S6 T3" }, "Thorn Bite", "Thorn Bite")]
    [InlineData(37UL, ItemRarity.Magic, new[] { "implicit 37", "P11 T1" }, null, "Hale Ironbound Chestguard")]
    public void Name_an_item_from_its_rarity_and_affixes(
        ulong template, ItemRarity rarity, string[] modifiers, string? rareName, string? expected)
    {
        List<ItemInstanceModifier> rolled =
        [
            .. modifiers.Select((key, ordinal) =>
                new ItemInstanceModifier { ModifierId = CatalogKeys.ModifierId(key), Ordinal = (byte)ordinal }),
        ];

        string? name = ItemNames.Display(s_data.FindItemTemplate(new ItemTemplateId(template))!, rarity, rareName, rolled,
            s_data.ItemModifiers);

        Assert.Equal(expected, name);
    }
}
