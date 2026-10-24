using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// #1179: what the items area leaves out of the modifier catalog rather than trust. Over a sound catalog (a
/// prefix that rolls on swords, a unique template linking its unique row, a common base linking its implicit row), one
/// breach at a time: the row or the template's links it breaks are left out, and a template whose links break its
/// rarity is left out whole; nothing else is.
/// </summary>
public class ItemModifierCatalogShould
{
    private const int Prefix = 1;
    private const int Unique = 2;
    private const int Implicit = 3;
    private const ulong UniqueTemplate = 10;
    private const ulong CommonTemplate = 11;

    [Theory]
    [InlineData("nothing", 0, 0ul, 0ul)]
    [InlineData("a modifier with no stat line", Prefix, 0ul, 0ul)]
    [InlineData("a prefix row without a tier", Prefix, 0ul, 0ul)]
    [InlineData("a unique row with a tier", Unique, 0ul, 0ul)]
    [InlineData("a weight on a unique", Unique, 0ul, 0ul)]
    [InlineData("a weight on an implicit", Implicit, 0ul, 0ul)]
    [InlineData("a link to a prefix", 0, CommonTemplate, 0ul)]
    [InlineData("a link to a missing modifier", 0, CommonTemplate, 0ul)]
    [InlineData("a unique linked beside another modifier", 0, UniqueTemplate, 0ul)]
    [InlineData("a common template linking a unique", 0, CommonTemplate, CommonTemplate)]
    [InlineData("a unique template linking an implicit", 0, UniqueTemplate, UniqueTemplate)]
    public void Leave_out_what_breaks_the_catalogs_rules(string breach, int leftOut, ulong linksLeftOut, ulong templateLeftOut)
    {
        ItemModifier prefix = Row(Prefix, ItemModifierKind.Prefix, tier: 1);
        prefix.Weights.Add(new ItemModifierWeight { ModifierId = Prefix, BaseKind = ItemBaseKind.Sword, Weight = 1000 });
        ItemModifier unique = Row(Unique, ItemModifierKind.Unique, tier: null);
        ItemModifier @implicit = Row(Implicit, ItemModifierKind.Implicit, tier: null);
        Dictionary<ulong, int[]> links = new() { [UniqueTemplate] = [Unique], [CommonTemplate] = [Implicit] };

        switch (breach)
        {
            case "a modifier with no stat line":
                prefix.Stats.Clear();
                break;
            case "a prefix row without a tier":
                prefix.Tier = null;
                break;
            case "a unique row with a tier":
                unique.Tier = 1;
                break;
            case "a weight on a unique":
                unique.Weights.Add(new ItemModifierWeight { ModifierId = Unique, BaseKind = ItemBaseKind.Ring, Weight = 1 });
                break;
            case "a weight on an implicit":
                @implicit.Weights.Add(new ItemModifierWeight { ModifierId = Implicit, BaseKind = ItemBaseKind.PlateChest, Weight = 1 });
                break;
            case "a link to a prefix":
                links[CommonTemplate] = [Implicit, Prefix];
                break;
            case "a link to a missing modifier":
                links[CommonTemplate] = [Implicit, 99];
                break;
            case "a unique linked beside another modifier":
                links[UniqueTemplate] = [Unique, Implicit];
                break;
            case "a common template linking a unique":
                links[CommonTemplate] = [Unique];
                links[UniqueTemplate] = [];
                break;
            case "a unique template linking an implicit":
                links[UniqueTemplate] = [Implicit];
                break;
        }

        ItemTemplate[] templates =
        [
            new() { Id = UniqueTemplate, Name = "Unique", Rarity = ItemRarity.Unique },
            new() { Id = CommonTemplate, Name = "Base", Rarity = ItemRarity.Common },
        ];
        ItemTemplateModifier[] rows =
        [
            .. links.SelectMany(pair => pair.Value.Select((modifier, index) =>
                new ItemTemplateModifier { TemplateId = pair.Key, Index = (byte)index, ModifierId = modifier })),
        ];
        var patch = ItemsPatch.Build(templates,
            new ItemModifierCatalog([prefix, unique, @implicit], rows, NullLogger.Instance), NullLogger.Instance);
        ItemModifierCatalog catalog = patch.Modifiers;

        // A template whose links break its rarity is left out whole, never kept with its links stripped.
        Assert.Equal(
            [UniqueTemplate != templateLeftOut, CommonTemplate != templateLeftOut],
            new[] { UniqueTemplate, CommonTemplate }.Select(id => patch.Find(new ItemTemplateId(id)) is not null));

        Assert.Equal(
            [Prefix != leftOut, Unique != leftOut, Implicit != leftOut],
            new[] { Prefix, Unique, Implicit }.Select(id => catalog.Find(id) is not null));
        Assert.Equal(Prefix != leftOut, catalog.CandidatesFor(ItemBaseKind.Sword).Any(c => c.Modifier.Id == Prefix));
        Assert.Equal(links[UniqueTemplate].Length > 0 && UniqueTemplate != linksLeftOut && Unique != leftOut,
            catalog.LinkedTo(new ItemTemplateId(UniqueTemplate)).Count > 0);
        Assert.Equal(CommonTemplate != linksLeftOut && Implicit != leftOut,
            catalog.LinkedTo(new ItemTemplateId(CommonTemplate)).Count > 0);
    }

    private static ItemModifier Row(int id, ItemModifierKind kind, byte? tier) => new()
    {
        Id = id,
        GroupId = id,
        Group = new ItemModifierGroup { Id = id, Kind = kind, Code = kind <= ItemModifierKind.Suffix ? "P1" : null, Name = kind.ToString() },
        Tier = tier,
        RequiredItemLevel = 1,
        Stats = [new ItemModifierStat { ModifierId = id, Index = 0, Stat = StatType.Health, Min = 1, Max = 2 }],
    };
}
