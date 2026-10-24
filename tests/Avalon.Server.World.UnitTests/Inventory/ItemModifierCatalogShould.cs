using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
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

    /// <summary>
    /// #1179: a template's own base-stat rows held to its kind (ItemBaseStatRules.Problem), beside a sound sword. The
    /// template that breaks it is left out whole, and the log names it; the sword stays.
    /// </summary>
    [Theory]
    [InlineData("a plate base without armour")]
    [InlineData("a ring carrying armour")]
    [InlineData("a weapon whose minimum reaches past its maximum")]
    [InlineData("a range above its own maximum")]
    [InlineData("a sword without a damage type")]
    [InlineData("a shield with a damage type")]
    public void Leave_out_a_template_whose_base_stats_break_its_kind(string breach)
    {
        const ulong Subject = 4242;
        ItemTemplate sword = Base(CommonTemplate, ItemBaseKind.Sword, DamageType.Physical,
            (ItemBaseStat.DamageMin, 2, 4), (ItemBaseStat.DamageMax, 6, 8), (ItemBaseStat.AttackTime, 665, 735));
        ItemTemplate broken = breach switch
        {
            "a plate base without armour" => Base(Subject, ItemBaseKind.PlateChest, null),
            "a ring carrying armour" => Base(Subject, ItemBaseKind.Ring, null, (ItemBaseStat.Armour, 1, 2)),
            "a weapon whose minimum reaches past its maximum" => Base(Subject, ItemBaseKind.Sword, DamageType.Physical,
                (ItemBaseStat.DamageMin, 5, 9), (ItemBaseStat.DamageMax, 8, 12), (ItemBaseStat.AttackTime, 665, 735)),
            "a range above its own maximum" => Base(Subject, ItemBaseKind.PlateChest, null, (ItemBaseStat.Armour, 10, 6)),
            "a sword without a damage type" => Base(Subject, ItemBaseKind.Sword, null,
                (ItemBaseStat.DamageMin, 2, 4), (ItemBaseStat.DamageMax, 6, 8), (ItemBaseStat.AttackTime, 665, 735)),
            _ => Base(Subject, ItemBaseKind.Shield, DamageType.Physical, (ItemBaseStat.Armour, 5, 7), (ItemBaseStat.BlockChance, 4, 6)),
        };
        var log = new TestLog();

        var patch = ItemsPatch.Build([sword, broken], new ItemModifierCatalog([], [], NullLogger.Instance), log);

        Assert.Null(patch.Find(new ItemTemplateId(Subject)));
        Assert.NotNull(patch.Find(new ItemTemplateId(CommonTemplate)));
        Assert.StartsWith($"Item template {Subject} is left out: ", Assert.Single(log.Errors).Message);
    }

    private static ItemTemplate Base(ulong id, ItemBaseKind kind, DamageType? damageType,
        params (ItemBaseStat Stat, int Min, int Max)[] ranges) => new()
        {
            Id = id,
            Name = kind.ToString(),
            Rarity = ItemRarity.Common,
            BaseKind = kind,
            DamageType1 = damageType,
            BaseStats = [.. ranges.Select(r => new ItemTemplateBaseStat { TemplateId = id, Stat = r.Stat, Min = r.Min, Max = r.Max })],
        };

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
