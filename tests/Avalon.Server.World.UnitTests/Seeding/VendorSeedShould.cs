using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.Dialogue;
using Avalon.World.Public.Dialogue;
using Avalon.World.Vendors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>
/// The three town vendors (spec #432) as the World database seeds them: the NPCs, their dialogue,
/// the tier-1 bases they sell (#1179), the Greater Health Potion, and the stock with its one cost row.
/// </summary>
public class VendorSeedShould
{
    private static readonly (ulong Id, string Name, string SubName)[] s_vendors =
    [
        (12, "Garrick Emberforge", "Weapons Dealer"),
        (13, "Hilde Brassbuckle", "Armourer"),
        (14, "Tobin Marrowfield", "Trade Goods"),
    ];

    /// <summary>Each tier-1 base the town sells and its tier-2 counterpart (#1179).</summary>
    private static readonly (ulong First, ulong Second)[] s_tiers =
    [
        (32, 7), (33, 5), (34, 6), (35, 8), (70, 71), (73, 74),
        (36, 12), (37, 13), (38, 14), (39, 15), (40, 16), (76, 79),
        (46, 22), (47, 23), (48, 24), (49, 25), (50, 26), (77, 80),
        (41, 17), (42, 18), (43, 19), (44, 20), (45, 21), (78, 81),
    ];

    private static Dictionary<ulong, ItemTemplate> Items(WorldDbContext context) =>
        context.ItemTemplates.AsNoTracking().ToList().ToDictionary(i => i.Id.Value);

    private static List<VendorStock> Stock(WorldDbContext context) =>
        context.VendorStocks.AsNoTracking().Include(s => s.Costs).ToList();

    private static Dictionary<StatType, uint> Stats(ItemTemplate t) =>
        new (StatType? Type, uint? Value)[]
            {
                (t.StatType1, t.StatValue1), (t.StatType2, t.StatValue2), (t.StatType3, t.StatValue3),
                (t.StatType4, t.StatValue4), (t.StatType5, t.StatValue5), (t.StatType6, t.StatValue6),
                (t.StatType7, t.StatValue7), (t.StatType8, t.StatValue8), (t.StatType9, t.StatValue9),
                (t.StatType10, t.StatValue10),
            }
            .Where(s => s.Type is not null)
            .ToDictionary(s => s.Type!.Value, s => s.Value ?? 0);

    [Fact]
    public void Seed_the_three_vendors_as_unkillable_town_npcs_that_drop_nothing()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var templates = context.CreatureTemplates.AsNoTracking().ToList();

        foreach ((ulong id, string name, string subName) in s_vendors)
        {
            CreatureTemplate vendor = templates.Single(t => t.Id.Value == id);
            Assert.Equal((name, subName), (vendor.Name, vendor.SubName));
            Assert.True(vendor.Invulnerable);
            Assert.Equal("TownNpcScript", vendor.ScriptName);
            Assert.Null(vendor.LootTableId);
            Assert.Equal(0u, vendor.Experience);
            Assert.Equal((0, 0), (vendor.MinGold, vendor.MaxGold));
        }
    }

    [Fact]
    public void Place_the_vendors_in_the_market_on_map_one_facing_its_centre()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var spawns = context.MapCreatureSpawns.AsNoTracking().ToList();

        (ulong Template, float X, float Z, float Facing)[] expected =
        [
            (12, 36f, 1.6f, 255f),
            (13, 37.4f, 0f, 270f),
            (14, 22f, 6f, 127f),
        ];

        foreach ((ulong template, float x, float z, float facing) in expected)
        {
            MapCreatureSpawn spawn = spawns.Single(s => s.CreatureTemplateId.Value == template);
            Assert.Equal(1u, spawn.MapTemplateId.Value);
            Assert.Equal((x, 0f, z, facing), (spawn.OffsetX, spawn.OffsetY, spawn.OffsetZ, spawn.Facing));
            Assert.Null(spawn.PathId);
        }
    }

    [Fact]
    public void Make_each_of_the_three_a_vendor_and_no_one_else()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var actions = new DialogueActions(context.DialogueNodes.AsNoTracking().ToList(), context.DialogueOptions.AsNoTracking().ToList());

        foreach ((ulong id, _, _) in s_vendors)
        {
            Assert.True(NpcInteraction.IsVendor(actions, new CreatureTemplateId(id)));
            Assert.False(NpcInteraction.IsBanker(actions, new CreatureTemplateId(id)));
        }

        foreach (ulong other in new ulong[] { 1, 2, 3, 11 })
            Assert.False(NpcInteraction.IsVendor(actions, new CreatureTemplateId(other)));
    }

    [Fact]
    public void Offer_trade_wares_and_farewell_and_keep_the_shop_open_on_both_nodes()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var nodes = context.DialogueNodes.AsNoTracking().ToList();
        var options = context.DialogueOptions.AsNoTracking().ToList();
        var catalog = new DialogueCatalog(nodes, options, NullLoggerFactory.Instance);
        var actions = new DialogueActions(nodes, options);

        foreach ((ulong id, _, _) in s_vendors)
        {
            DialogueNodeView root = catalog.GetRoot(new CreatureTemplateId(id))!;
            Assert.Equal(3, root.Options.Count);
            DialogueOptionView deal = root.Options[0], wares = root.Options[1], farewell = root.Options[2];

            Assert.Equal(23, deal.TextId.Value);                 // "What do you deal in?"
            Assert.Null(actions.For(deal.Id));
            DialogueNodeView trade = catalog.GetNode(deal.NextNodeId!)!;
            Assert.Equal(new CreatureTemplateId(id), trade.CreatureTemplateId);

            Assert.Equal(24, wares.TextId.Value);                // "Show me your wares."
            Assert.Equal(DialogueOptionAction.OpenShop, actions.For(wares.Id));
            Assert.Equal(root.Id, wares.NextNodeId);             // stays on the node: the shop stays open

            Assert.Equal(10, farewell.TextId.Value);             // the shared "Farewell."
            Assert.Null(farewell.NextNodeId);

            Assert.Equal(2, trade.Options.Count);
            Assert.Equal(DialogueOptionAction.OpenShop, actions.For(trade.Options[0].Id));
            Assert.Equal(trade.Id, trade.Options[0].NextNodeId);
            Assert.Equal(10, trade.Options[1].TextId.Value);
            Assert.Null(trade.Options[1].NextNodeId);
        }
    }

    [Fact]
    public void Write_and_translate_every_vendor_line()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        var texts = context.LocalizedTexts.AsNoTracking().ToList().ToDictionary(t => t.Id.Value, t => t.Text);
        var locales = context.LocalizedTextLocales.AsNoTracking().ToList();

        Assert.Equal("Steel, stave or string, traveller. What'll it be?", texts[17]);
        Assert.Equal("Every blade here I hammered myself. Won't match what the forest spits out, but it'll keep you breathing till you find better.", texts[18]);
        Assert.Equal("Mind the rack. Looking to cover something?", texts[19]);
        Assert.Equal("Plate, leather, cloth. I fit every trade. Buy it plain, earn it fancy.", texts[20]);
        Assert.Equal("Potions, scrolls, supplies. And I'll take what you've no use for.", texts[21]);
        Assert.Equal("I buy anything that isn't nailed to you. Fair prices, mostly.", texts[22]);
        Assert.Equal("What do you deal in?", texts[23]);
        Assert.Equal("Show me your wares.", texts[24]);

        for (int id = 17; id <= 24; id++)
            Assert.Contains(locales, l => l.TextId.Value == id && l.Locale == AccountLocale.ptPT && l.Text.Length > 0);
    }

    /// <summary>
    /// #1179: what Garrick and Hilde sell is the twenty-four tier-1 bases, Common, level 1, for every class, sellable for
    /// a quarter of their price (rounded down).
    /// </summary>
    [Fact]
    public void Seed_the_starter_tier_as_twenty_four_common_tier_one_bases_for_every_class()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        Dictionary<ulong, ItemTemplate> items = Items(context);
        ulong[] sold = [.. Stock(context).Where(r => r.CreatureTemplateId.Value is 12 or 13).Select(r => r.ItemTemplateId.Value)];

        Assert.Equal(s_tiers.Select(t => t.First).Order(), sold.Order());
        foreach (ulong id in sold)
        {
            ItemTemplate item = items[id];
            Assert.Equal(ItemRarity.Common, item.Rarity);
            Assert.NotEqual(ItemBaseKind.None, item.BaseKind);
            Assert.Empty(item.AllowedClasses);
            Assert.Equal(item.BuyPrice / 4, item.SellPrice);
            Assert.False(item.Flags.HasFlag(ItemTemplateFlags.NoSell), $"{item.Name} is marked NoSell");
            Assert.Equal((1u, (ushort?)1), (item.MaxStackSize, item.RequiredLevel));
        }
    }

    /// <summary>
    /// #1179: each tier-1 base is the same kind as its tier-2 counterpart, with the same lines, and weaker: lower values on
    /// every line, a lower weapon roll and a lower requirement and price.
    /// </summary>
    [Fact]
    public void Make_every_tier_one_base_weaker_than_its_tier_two_counterpart()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        Dictionary<ulong, ItemTemplate> items = Items(context);

        foreach ((ulong firstId, ulong secondId) in s_tiers)
        {
            ItemTemplate first = items[firstId], second = items[secondId];

            Assert.Equal((second.BaseKind, second.Class, second.SubClass, second.Slot, second.AttackTimeMs),
                (first.BaseKind, first.Class, first.SubClass, first.Slot, first.AttackTimeMs));
            Dictionary<StatType, uint> firstStats = Stats(first), secondStats = Stats(second);
            Assert.Equal(secondStats.Keys, firstStats.Keys);
            Assert.All(firstStats, s => Assert.True(s.Value < secondStats[s.Key], $"{first.Name} {s.Key} is not below {second.Name}'s"));

            if (second.DamageMax1 is { } max)
            {
                Assert.True(first.DamageMin1 < second.DamageMin1 && first.DamageMax1 < max, $"{first.Name} does not roll below {second.Name}");
                Assert.Equal(second.DamageType1, first.DamageType1);
            }
            else
            {
                Assert.Null(first.DamageMax1);
            }

            Assert.True(first.RequiredLevel < second.RequiredLevel && first.BuyPrice < second.BuyPrice,
                $"{first.Name} is not a lower tier than {second.Name}");
        }
    }

    [Fact]
    public void Seed_the_greater_health_potion_like_the_health_potion()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        Dictionary<ulong, ItemTemplate> items = Items(context);
        ItemTemplate greater = items[56], health = items[1];

        Assert.Equal("Greater Health Potion", greater.Name);
        Assert.Equal((health.Class, health.SubClass, health.Flags, health.MaxStackSize, health.Rarity, health.Slot),
            (greater.Class, greater.SubClass, greater.Flags, greater.MaxStackSize, greater.Rarity, greater.Slot));
        Assert.Equal((25u, 12u, 56u), (greater.BuyPrice, greater.SellPrice, greater.DisplayId));
    }

    [Fact]
    public void Stock_every_vendor_as_the_spec_lists()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        List<VendorStock> rows = Stock(context);

        ulong[] ItemsOf(ulong vendor) => rows
            .Where(r => r.CreatureTemplateId.Value == vendor)
            .OrderBy(r => r.Sequence)
            .Select(r => r.ItemTemplateId.Value)
            .ToArray();

        // #1179: Garrick the six tier-1 weapon and off-hand bases (sword, mace, staff, bow, shield, caster off-hand);
        // Hilde the eighteen tier-1 armour bases, plate, leather, cloth, each head, chest, legs, hands, feet, shoulders.
        Assert.Equal(31, rows.Count);
        Assert.Equal([32UL, 35, 33, 34, 70, 73], ItemsOf(12));
        Assert.Equal([36UL, 37, 38, 39, 40, 76, 46, 47, 48, 49, 50, 77, 41, 42, 43, 44, 45, 78], ItemsOf(13));
        Assert.Equal([1UL, 2, 3, 9, 10, 11, 56], ItemsOf(14));

        Assert.Equal(
            [(37UL, 2u, 1800u), (42UL, 2u, 1800u), (47UL, 2u, 1800u), (56UL, 5u, 600u)],
            rows.Where(r => r.MaxStock is not null)
                .OrderBy(r => r.ItemTemplateId.Value)
                .Select(r => (r.ItemTemplateId.Value, r.MaxStock!.Value, r.RestockSeconds!.Value)));
        Assert.All(rows, r => Assert.Null(r.PriceOverride));
    }

    [Fact]
    public void Cost_the_greater_potion_two_health_potions()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        List<VendorStock> rows = Stock(context);

        VendorStock greater = rows.Single(r => r.ItemTemplateId.Value == 56);
        VendorStockCost cost = Assert.Single(greater.Costs);
        Assert.Equal((1UL, 2u), (cost.ItemTemplateId.Value, cost.Count));
        Assert.All(rows.Where(r => r.Id != greater.Id), r => Assert.Empty(r.Costs));
    }

    [Fact]
    public void Load_every_seeded_stock_row_without_refusing_any()
    {
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var catalog = new VendorCatalog(Stock(context), Items(context).Values.ToList(), NullLoggerFactory.Instance);

        Assert.Empty(catalog.Refused);
        Assert.Equal((3, 31), (catalog.VendorCount, catalog.RowCount));
    }
}
