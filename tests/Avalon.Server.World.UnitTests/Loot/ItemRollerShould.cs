using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Seeding;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>
/// The roll of a made item (#1179) over the seeded catalog and templates: rarity by the creature's odds, the best base tier
/// the item level reaches, the implicits first, then affixes drawn by weight within what the rarity and item level allow,
/// uniques within their ranges, and the boss kill's one rare.
/// </summary>
public class ItemRollerShould
{
    // The spec's rare-name words: the first word, then the second by the item's kind.
    private static readonly string[] s_firstWords =
        ["Thorn", "Moss", "Ash", "Fen", "Bramble", "Grim", "Dusk", "Root", "Bark", "Hollow", "Ember", "Blight", "Gloam", "Wild", "Stag", "Mire"];
    private static readonly string[] s_weaponWords = ["Bite", "Fang", "Edge", "Song", "Spine", "Sting"];
    private static readonly string[] s_armourWords = ["Shell", "Hide", "Ward", "Bark", "Mantle", "Coat"];
    private static readonly string[] s_jewelleryWords = ["Eye", "Heart", "Knot", "Coil", "Seed", "Tear"];

    private static readonly StaticData s_data = ScenarioReferenceData.Shared.Data;
    private static readonly ItemsPatch s_items = new(s_data.ItemTemplates, s_data.ItemModifiers);

    private static ItemModifierCatalog Catalog => s_items.Modifiers;

    private static ItemRoller Roller(ILootRandom random) =>
        new(random, new ItemIdAllocator(), Options.Create(new GameConfiguration()));

    private static ItemTemplate Template(ulong id) => s_items.Find(new ItemTemplateId(id))!;

    [Theory]
    [InlineData(CreatureRarity.Normal, 0.7499, ItemRarity.Common)]
    [InlineData(CreatureRarity.Normal, 0.75, ItemRarity.Magic)]
    [InlineData(CreatureRarity.Normal, 0.97, ItemRarity.Rare)]
    [InlineData(CreatureRarity.Elite, 0.5999, ItemRarity.Common)]
    [InlineData(CreatureRarity.Elite, 0.92, ItemRarity.Rare)]
    [InlineData(CreatureRarity.Rare, 0.45, ItemRarity.Magic)]
    [InlineData(CreatureRarity.Boss, 0.4999, ItemRarity.Magic)]
    [InlineData(CreatureRarity.Boss, 0.5, ItemRarity.Rare)]
    public void Roll_rarity_by_the_creatures_odds(CreatureRarity odds, double draw, ItemRarity expected)
    {
        RolledItem rolled = Roller(EdgeRandom.Low(draw)).Roll(Template(37), new DropContext(1, odds, false), s_items)!;

        Assert.Equal(expected, rolled.Rarity);
    }

    [Theory]
    [InlineData(37UL, (ushort)0, 37UL)]   // no level (a quest without a requirement): the roll makes it 1, as a save requires
    [InlineData(37UL, (ushort)1, 37UL)]
    [InlineData(37UL, (ushort)4, 37UL)]
    [InlineData(37UL, (ushort)5, 13UL)]
    [InlineData(37UL, (ushort)9, 13UL)]
    [InlineData(37UL, (ushort)10, 86UL)]
    [InlineData(13UL, (ushort)2, 37UL)]
    [InlineData(68UL, (ushort)15, 68UL)]
    [InlineData(58UL, (ushort)1, 58UL)]
    public void Pick_the_best_base_tier_the_item_level_reaches(ulong named, ushort itemLevel, ulong dropped)
    {
        int[] implicits = [.. CatalogKeys.All().Where(row => row.Key == $"implicit {dropped}").Select(row => row.ModifierId)];

        RolledItem rolled = Roller(EdgeRandom.Low())
            .Roll(Template(named), new DropContext(itemLevel, CreatureRarity.Normal, false), s_items)!;

        Assert.Equal(dropped, rolled.TemplateId.Value);
        Assert.Equal(Math.Max((ushort)1, itemLevel), rolled.ItemLevel);
        Assert.Equal(implicits, rolled.Modifiers.Take(implicits.Length).Select(m => m.ModifierId));
        Assert.DoesNotContain(rolled.Modifiers.Skip(implicits.Length),
            m => Catalog.Find(m.ModifierId)!.Group.Kind == ItemModifierKind.Implicit);
    }

    [Fact]
    public void Roll_only_what_the_weights_and_the_rarity_allow()
    {
        ItemRoller roller = Roller(new LootRandom(new Random(1179)));
        HashSet<Guid> ids = [];
        bool threeResistances = false;

        foreach (ItemBaseKind kind in Enum.GetValues<ItemBaseKind>())
        {
            if (s_items.LineOf(kind) is not [{ } named, ..])
                continue;

            foreach ((ushort itemLevel, int rolls) in new (ushort, int)[] { (15, 2000), (1, 300), (3, 300) })
            {
                for (int i = 0; i < rolls; i++)
                {
                    var context = new DropContext(itemLevel, (CreatureRarity)(i % 4), false);
                    RolledItem rolled = roller.Roll(named, context, s_items)!;
                    AssertWithinTheRules(rolled, kind, itemLevel, ids);
                    threeResistances |= kind == ItemBaseKind.Ring && rolled.Rarity == ItemRarity.Rare
                                        && rolled.Modifiers.Count(m => Catalog.Find(m.ModifierId)!.Group.Code == "S5") == 3;
                }
            }
        }

        Assert.True(threeResistances, "no rare ring carried three resistance families");
    }

    [Fact]
    public void Make_one_drop_rare_when_a_boss_kill_rolled_none()
    {
        // Every draw at its lowest: under the Boss odds each base rolls Magic with one affix. The chest drop names two
        // copies; a rolled drop is one item.
        var context = new DropContext(5, CreatureRarity.Boss, false);
        List<RolledDrop> drops = [Drop(65), RolledDrop.GoldPile(12), RolledDrop.Item(new ItemTemplateId(37), 2), Drop(68)];

        Roller(EdgeRandom.Low()).RollDrops(drops, context, s_items);

        Assert.Equal(4, drops.Count);
        RolledItem unique = drops[0].Rolled!, upgraded = drops[2].Rolled!, magic = drops[3].Rolled!;
        Assert.Equal(ItemRarity.Unique, unique.Rarity);
        Assert.Equal(Catalog.LinkedTo(Template(65).Id).Single().Stats.Select(line => line.Min),
            unique.Modifiers.Single().Values.Select(value => value.Value));
        Assert.Null(drops[1].Rolled);
        Assert.Equal(12UL, drops[1].Gold);

        Assert.Equal(ItemRarity.Rare, upgraded.Rarity);
        Assert.Equal((13UL, 1u), (drops[2].ItemTemplateId!.Value, drops[2].Count));
        Assert.Equal(CatalogKeys.ModifierId("implicit 13"), upgraded.Modifiers[0].ModifierId);
        Assert.Equal(1 + 3, upgraded.Modifiers.Count);
        Assert.Equal("Thorn Shell", upgraded.RareName);
        Assert.Equal(ItemRarity.Magic, magic.Rarity);

        // A kill whose first base already rolled Rare upgrades nothing else. Every draw is scripted, in the contracted
        // order, so an extra, missing or reordered draw fails. Each pick of 0.0 lands on the first eligible row.
        // The plate chest (13 at item level 5): rarity 0.5 (Rare), its implicit, the count 3, three prefixes (the next
        // prefix group each time), their values, then the two words. The ring: rarity 0.0 (Magic), the count 2, a prefix,
        // then a suffix (the prefix room is full), and their values.
        ItemModifierStat Line(string key) => Catalog.Find(CatalogKeys.ModifierId(key))!.Stats.Single();
        var script = new ScriptedLootRandom(
            [0.5, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
            [Line("implicit 13").Min, 3, Line("P11 T1").Min, Line("P12 T1").Min, Line("P13 T1").Min, 0, 0,
             2, Line("P8 Fire T1").Min, Line("S1 T1").Min]);
        List<RolledDrop> again = [Drop(37), Drop(68)];

        Roller(script).RollDrops(again, context, s_items);

        Assert.True(script.Exhausted, "the roll left scripted draws unused");
        Assert.Equal([ItemRarity.Rare, ItemRarity.Magic], again.Select(drop => drop.Rolled!.Rarity));
        Assert.Equal("Thorn Shell", again[0].Rolled!.RareName);
        Assert.Equal(
            [
                (string[])["implicit 13", "P11 T1", "P12 T1", "P13 T1"],
                ["P8 Fire T1", "S1 T1"],
            ],
            again.Select(drop => drop.Rolled!.Modifiers.Select(m => KeyOf(m.ModifierId)).ToArray()));
    }

    private static string KeyOf(int modifierId) => CatalogKeys.All().Single(row => row.ModifierId == modifierId).Key;

    [Fact]
    public void Drop_no_unique_whose_modifier_the_catalog_left_out()
    {
        var items = new ItemsPatch(s_data.ItemTemplates, Catalog.WithoutLinksOf(new HashSet<ulong> { 65 }));
        List<RolledDrop> drops = [Drop(65), Drop(64)];

        Roller(EdgeRandom.Low()).RollDrops(drops, new DropContext(5, CreatureRarity.Boss, false), items);

        Assert.Equal(64UL, Assert.Single(drops).ItemTemplateId!.Value);
    }

    [Fact]
    public void Roll_each_unique_as_its_one_modifier_within_its_ranges()
    {
        ItemRoller roller = Roller(new LootRandom(new Random(1179)));
        var context = new DropContext(5, CreatureRarity.Normal, false);

        foreach (ulong id in new ulong[] { 58, 64, 65, 66, 67 })
        {
            ItemModifier unique = Catalog.Find(CatalogKeys.ModifierId($"unique {id}"))!;
            for (int i = 0; i < 500; i++)
            {
                RolledItem rolled = roller.Roll(Template(id), context, s_items)!;

                Assert.Equal(ItemRarity.Unique, rolled.Rarity);
                Assert.Equal(id, rolled.TemplateId.Value);
                Assert.Null(rolled.RareName);
                ItemInstanceModifier modifier = Assert.Single(rolled.Modifiers);
                Assert.Equal((unique.Id, (byte)0), (modifier.ModifierId, modifier.Ordinal));
                AssertValuesWithinLines(unique, modifier);
            }
        }

        StatType adds = StatType.AddedPhysicalDamageToAttacks;
        Assert.Equal((2, 3), Values(Roller(EdgeRandom.Low()), adds));
        Assert.Equal((2, 5), Values(Roller(EdgeRandom.High()), adds));

        static (int, int?) Values(ItemRoller roller, StatType stat)
        {
            RolledItem rolled = roller.Roll(Template(65), new DropContext(5, CreatureRarity.Normal, false), s_items)!;
            ItemInstanceModifierValue value = rolled.Modifiers.Single().Values.Single(v => v.Stat == stat);
            return (value.Value, value.Value2);
        }
    }

    [Fact]
    public void Roll_nothing_for_an_item_without_a_kind() =>
        Assert.Null(Roller(new ScriptedLootRandom([])).Roll(Template(1), new DropContext(5, CreatureRarity.Boss, false), s_items));

    private static RolledDrop Drop(ulong template) => RolledDrop.Item(new ItemTemplateId(template), 1);

    private static void AssertWithinTheRules(RolledItem rolled, ItemBaseKind kind, ushort itemLevel, HashSet<Guid> ids)
    {
        IReadOnlyList<ItemModifier> implicits = Catalog.LinkedTo(rolled.TemplateId);
        Assert.Equal(itemLevel, rolled.ItemLevel);
        Assert.Equal(implicits.Select(m => m.Id), rolled.Modifiers.Take(implicits.Count).Select(m => m.ModifierId));
        Assert.Equal(Enumerable.Range(0, rolled.Modifiers.Count), rolled.Modifiers.Select(m => (int)m.Ordinal));

        List<ItemModifier> affixes = [.. rolled.Modifiers.Skip(implicits.Count).Select(m => Catalog.Find(m.ModifierId)!)];
        foreach (ItemModifier affix in affixes)
        {
            Assert.True(affix.Weights.Any(w => w.BaseKind == kind && w.Weight > 0), $"modifier {affix.Id} has no weight on {kind}");
            Assert.True(affix.RequiredItemLevel <= itemLevel, $"modifier {affix.Id} needs item level {affix.RequiredItemLevel}");
        }

        Assert.Equal(affixes.Count, affixes.Select(m => m.GroupId).Distinct().Count());
        int prefixes = affixes.Count(m => m.Group.Kind == ItemModifierKind.Prefix);
        int suffixes = affixes.Count(m => m.Group.Kind == ItemModifierKind.Suffix);
        Assert.Equal(affixes.Count, prefixes + suffixes);
        (int least, int most, int room) = rolled.Rarity switch
        {
            ItemRarity.Common => (0, 0, 0),
            ItemRarity.Magic => (1, 2, 1),
            ItemRarity.Rare => (3, 6, 3),
            _ => throw new Xunit.Sdk.XunitException($"a base rolled {rolled.Rarity}"),
        };
        Assert.InRange(affixes.Count, least, most);
        Assert.InRange(prefixes, 0, room);
        Assert.InRange(suffixes, 0, room);

        foreach (ItemInstanceModifier modifier in rolled.Modifiers)
        {
            Assert.Equal(rolled.InstanceId, modifier.ItemInstanceId);
            Assert.True(ids.Add(modifier.Id), $"modifier id {modifier.Id} handed out twice");
            AssertValuesWithinLines(Catalog.Find(modifier.ModifierId)!, modifier);
        }

        if (rolled.Rarity != ItemRarity.Rare)
        {
            Assert.Null(rolled.RareName);
            return;
        }

        string[] second = kind switch
        {
            ItemBaseKind.Sword or ItemBaseKind.Mace or ItemBaseKind.Staff or ItemBaseKind.Bow => s_weaponWords,
            ItemBaseKind.Amulet or ItemBaseKind.Ring or ItemBaseKind.CasterOffHand => s_jewelleryWords,
            _ => s_armourWords,
        };
        string[] words = rolled.RareName!.Split(' ');
        Assert.Equal(2, words.Length);
        Assert.Contains(words[0], s_firstWords);
        Assert.Contains(words[1], second);
    }

    private static void AssertValuesWithinLines(ItemModifier catalogRow, ItemInstanceModifier modifier)
    {
        Assert.Equal(catalogRow.Stats.Select(line => (line.Index, line.Stat)), modifier.Values.Select(v => (v.Index, v.Stat)));
        foreach ((ItemModifierStat line, ItemInstanceModifierValue value) in catalogRow.Stats.Zip(modifier.Values))
        {
            Assert.Equal(modifier.Id, value.ItemInstanceModifierId);
            Assert.InRange(value.Value, line.Min, line.Max);
            Assert.Equal(line.Min2 is not null, value.Value2 is not null);
            if (value.Value2 is { } high)
            {
                Assert.InRange(high, line.Min2!.Value, line.Max2!.Value);
                Assert.True(high >= value.Value, $"modifier {catalogRow.Id}'s high end {high} is below its low end {value.Value}");
            }
        }
    }

    /// <summary>
    /// Every number at one end of its range: the lowest (or highest) integer, and the listed doubles first, then 0.0 for
    /// each later double (the first eligible row of a weighted pick).
    /// </summary>
    private sealed class EdgeRandom(bool high, double[] doubles) : ILootRandom
    {
        private readonly Queue<double> _doubles = new(doubles);

        public static EdgeRandom Low(params double[] doubles) => new(false, doubles);

        public static EdgeRandom High(params double[] doubles) => new(true, doubles);

        public double NextDouble() => _doubles.TryDequeue(out double next) ? next : 0.0;

        public long NextInt64(long minInclusive, long maxExclusive) => high ? maxExclusive - 1 : minInclusive;
    }
}
