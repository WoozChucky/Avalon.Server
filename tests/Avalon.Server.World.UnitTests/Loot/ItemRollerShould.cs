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

    /// <summary>
    /// #1179: a named base becomes the best tier of its own line the item level reaches, with that tier's implicit first.
    /// A named ring or amulet keeps its line (the Copper Band's, the Hearthstone Ring's, the Bone Charm's): only a drop
    /// draws one.
    /// </summary>
    [Theory]
    [InlineData(37UL, (ushort)0, 37UL)]   // no level (a quest without a requirement): the roll makes it 1, as a save requires
    [InlineData(37UL, (ushort)1, 37UL)]
    [InlineData(37UL, (ushort)4, 37UL)]
    [InlineData(37UL, (ushort)5, 13UL)]
    [InlineData(37UL, (ushort)9, 13UL)]
    [InlineData(37UL, (ushort)10, 86UL)]
    [InlineData(13UL, (ushort)2, 37UL)]
    [InlineData(68UL, (ushort)4, 68UL)]
    [InlineData(68UL, (ushort)5, 104UL)]
    [InlineData(68UL, (ushort)10, 105UL)]
    [InlineData(112UL, (ushort)10, 114UL)]
    [InlineData(69UL, (ushort)10, 131UL)]
    [InlineData(58UL, (ushort)1, 58UL)]
    public void Pick_the_best_base_tier_the_item_level_reaches(ulong named, ushort itemLevel, ulong dropped)
    {
        RolledItem rolled = Roller(EdgeRandom.Low())
            .Roll(Template(named), new DropContext(itemLevel, CreatureRarity.Normal, false), s_items)!;

        // The base's one implicit, the dropped tier's own, comes first; a unique's is its base's.
        ItemTemplate @base = s_items.BaseOf(Template(dropped))!;
        Assert.Equal(dropped, rolled.TemplateId.Value);
        Assert.Equal(Math.Max((ushort)1, itemLevel), rolled.ItemLevel);
        Assert.Equal((Assert.Single(Catalog.LinkedTo(@base.Id)).Id, (byte)0), (rolled.Modifiers[0].ModifierId, rolled.Modifiers[0].Ordinal));
        Assert.DoesNotContain(rolled.Modifiers.Skip(1), m => Catalog.Find(m.ModifierId)!.Group.Kind == ItemModifierKind.Implicit);
    }

    /// <summary>
    /// #1179: a ring or amulet drop draws its line first, uniformly among its kind's lines (by their first tier's id),
    /// then rolls that line's best tier the item level reaches, whichever line the loot entry named; a level-1 drop stays
    /// the line's first tier. The plate chest's single line draws nothing (the strict script of
    /// <see cref="Make_one_drop_rare_when_a_boss_kill_rolled_none" />).
    /// </summary>
    [Theory]
    [InlineData(68UL, (ushort)5, 3L, 113UL)]   // the Hearthstone line at T8, the Kilnstone Ring
    [InlineData(69UL, (ushort)10, 6L, 149UL)]  // the Dewdrop line at T7, the Wellspring Pendant
    [InlineData(68UL, (ushort)1, 0L, 68UL)]
    public void Draw_a_jewellery_drops_line_then_its_best_tier(ulong named, ushort itemLevel, long lineDraw, ulong dropped)
    {
        var random = EdgeRandom.LowAfter(lineDraw);
        List<RolledDrop> drops = [Drop(named)];

        Roller(random).RollDrops(drops, new DropContext(itemLevel, CreatureRarity.Normal, false), s_items);

        Assert.Equal((0L, (long)s_items.LinesOf(Template(named).BaseKind!.Value).Count), random.FirstIntegerRange!.Value);
        Assert.Equal(dropped, Assert.Single(drops).Rolled!.TemplateId.Value);
    }

    [Fact]
    public void Roll_only_what_the_weights_and_the_rarity_allow()
    {
        ItemRoller roller = Roller(new LootRandom(new Random(1179)));
        HashSet<Guid> ids = [];
        bool threeResistances = false;

        foreach (ItemBaseKind kind in Enum.GetValues<ItemBaseKind>())
        {
            // A trinket base is made only as a unique's base, never rolled as a drop.
            if (kind == ItemBaseKind.Trinket || s_items.LinesOf(kind) is not [[{ } named, ..], ..])
                continue;

            foreach ((ushort itemLevel, int rolls) in new (ushort, int)[] { (15, 2000), (1, 300), (3, 300) })
            {
                for (int i = 0; i < rolls; i++)
                {
                    var context = new DropContext(itemLevel, (CreatureRarity)(i % 4), false);
                    RolledItem rolled = roller.Roll(named, context, s_items)!;
                    AssertWithinTheRules(rolled, kind, itemLevel, ids);
                    threeResistances |= kind == ItemBaseKind.Amulet && rolled.Rarity == ItemRarity.Rare
                                        && rolled.Modifiers.Count(m => Catalog.Find(m.ModifierId)!.Group.Code == "S5") == 3;
                }
            }
        }

        Assert.True(threeResistances, "no rare amulet carried three resistance families");
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
            unique.Modifiers[1].Values.Select(value => value.Value));
        Assert.Null(drops[1].Rolled);
        Assert.Equal(12UL, drops[1].Gold);

        Assert.Equal(ItemRarity.Rare, upgraded.Rarity);
        Assert.Equal((13UL, 1u), (drops[2].ItemTemplateId!.Value, drops[2].Count));
        Assert.Equal(new ItemBaseStats { Armour = Armour(13).Min }, upgraded.BaseStats);
        Assert.Equal(1 + 3, upgraded.Modifiers.Count);
        Assert.Equal("Thorn Shell", upgraded.RareName);
        Assert.Equal(ItemRarity.Magic, magic.Rarity);

        // A kill whose first base already rolled Rare upgrades nothing else. Every draw is scripted, in the contracted
        // order, so an extra, missing or reordered draw fails. Each pick of 0.0 lands on the first eligible row.
        // The plate chest (13 at item level 5), one line, so no line draw: rarity 0.5 (Rare), its armour, its implicit,
        // the count 3, three prefixes (the next prefix group each time), their values, then the two words. The ring:
        // its line (0, the Copper Band's, so the Oakbark Band at T8), rarity 0.0 (Magic), no base stats, its implicit, the
        // count 2, a prefix, then a suffix (the prefix room is full), and their values.
        ItemModifierStat Line(string key) => Catalog.Find(CatalogKeys.ModifierId(key))!.Stats.Single();
        var script = new ScriptedLootRandom(
            [0.5, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
            [Armour(13).Max, Line("implicit IncreasedMaximumLife T8").Min, 3, Line("P11 T9").Min, Line("P12 T9").Min,
             Line("P13 T9").Min, 0, 0,
             0, Line("implicit Health T8").Min, 2, Line("P8 Fire T9").Min, Line("S1 T9").Min]);
        List<RolledDrop> again = [Drop(37), Drop(68)];

        Roller(script).RollDrops(again, context, s_items);

        Assert.True(script.Exhausted, "the roll left scripted draws unused");
        Assert.Equal([ItemRarity.Rare, ItemRarity.Magic], again.Select(drop => drop.Rolled!.Rarity));
        Assert.Equal([13UL, 104UL], again.Select(drop => drop.Rolled!.TemplateId.Value));
        Assert.Equal("Thorn Shell", again[0].Rolled!.RareName);
        Assert.Equal([new ItemBaseStats { Armour = Armour(13).Max }, default],
            again.Select(drop => drop.Rolled!.BaseStats));
        Assert.Equal(
            [
                (string[])["implicit IncreasedMaximumLife T8", "P11 T9", "P12 T9", "P13 T9"],
                ["implicit Health T8", "P8 Fire T9", "S1 T9"],
            ],
            again.Select(drop => drop.Rolled!.Modifiers.Select(m => KeyOf(m.ModifierId)).ToArray()));
    }

    private static string KeyOf(int modifierId) => CatalogKeys.All().Single(row => row.ModifierId == modifierId).Key;

    private static ItemTemplateBaseStat Armour(ulong template) =>
        Template(template).BaseStats.Single(range => range.Stat == ItemBaseStat.Armour);

    [Fact]
    public void Drop_no_unique_whose_modifier_the_catalog_left_out()
    {
        var items = new ItemsPatch(s_data.ItemTemplates, Catalog.WithoutLinksOf(new HashSet<ulong> { 65 }));
        List<RolledDrop> drops = [Drop(65), Drop(64)];

        Roller(EdgeRandom.Low()).RollDrops(drops, new DropContext(5, CreatureRarity.Boss, false), items);

        Assert.Equal(64UL, Assert.Single(drops).ItemTemplateId!.Value);
    }

    /// <summary>
    /// #1179: a unique's copy rolls the base it names first, its base stats (none: jewellery and trinkets carry none) and
    /// its implicit at ordinal 0, then its own modifier at ordinal 1, every value within its line. The Thornheart Signet's
    /// "adds 1 to 3" names its ends exactly, so the lowest and the highest rolls both give 1 to 3.
    /// </summary>
    [Fact]
    public void Roll_each_unique_with_its_bases_implicit_and_its_own_lines()
    {
        ItemRoller roller = Roller(new LootRandom(new Random(1179)));
        var context = new DropContext(5, CreatureRarity.Normal, false);

        foreach (ulong id in new ulong[] { 58, 64, 65, 66, 67 })
        {
            ItemModifier unique = Catalog.Find(CatalogKeys.ModifierId($"unique {id}"))!;
            ItemModifier inherited = Assert.Single(Catalog.LinkedTo(Template(id).BaseTemplateId!));
            for (int i = 0; i < 500; i++)
            {
                RolledItem rolled = roller.Roll(Template(id), context, s_items)!;

                Assert.Equal(ItemRarity.Unique, rolled.Rarity);
                Assert.Equal(id, rolled.TemplateId.Value);
                Assert.Null(rolled.RareName);
                Assert.True(rolled.BaseStats.IsEmpty);
                Assert.Equal([(inherited.Id, (byte)0), (unique.Id, (byte)1)],
                    rolled.Modifiers.Select(m => (m.ModifierId, m.Ordinal)));
                AssertValuesWithinLines(inherited, rolled.Modifiers[0]);
                AssertValuesWithinLines(unique, rolled.Modifiers[1]);
            }
        }

        StatType adds = StatType.AddedPhysicalDamageToAttacks;
        Assert.Equal((1, 3), Values(Roller(EdgeRandom.Low()), adds));
        Assert.Equal((1, 3), Values(Roller(EdgeRandom.High()), adds));

        static (int, int?) Values(ItemRoller roller, StatType stat)
        {
            RolledItem rolled = roller.Roll(Template(65), new DropContext(5, CreatureRarity.Normal, false), s_items)!;
            ItemInstanceModifierValue value = rolled.Modifiers[1].Values.Single(v => v.Stat == stat);
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

        // Exactly the base stats its kind carries, each within the rolled template's range.
        ItemTemplate template = Template(rolled.TemplateId.Value);
        List<ItemBaseStat> stats = [];
        foreach ((ItemBaseStat stat, int value) in rolled.BaseStats)
        {
            stats.Add(stat);
            ItemTemplateBaseStat range = template.BaseStats.Single(r => r.Stat == stat);
            Assert.InRange(value, range.Min, range.Max);
        }

        Assert.Equal(ItemBaseStatRules.Of(template.BaseKind, template.Rarity), stats);
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
    /// each later double (the first eligible row of a weighted pick). <c>integers</c> are handed out first, as they are
    /// (a jewellery drop's line), and the range the first integer draw asked for is kept.
    /// </summary>
    private sealed class EdgeRandom(bool high, double[] doubles, long[] integers) : ILootRandom
    {
        private readonly Queue<double> _doubles = new(doubles);
        private readonly Queue<long> _integers = new(integers);

        public static EdgeRandom Low(params double[] doubles) => new(false, doubles, []);

        public static EdgeRandom LowAfter(params long[] integers) => new(false, [], integers);

        public static EdgeRandom High(params double[] doubles) => new(true, doubles, []);

        /// <summary>The range of the first NextInt64 request, or null before any.</summary>
        public (long Min, long MaxExclusive)? FirstIntegerRange { get; private set; }

        public double NextDouble() => _doubles.TryDequeue(out double next) ? next : 0.0;

        public long NextInt64(long minInclusive, long maxExclusive)
        {
            FirstIntegerRange ??= (minInclusive, maxExclusive);
            return _integers.TryDequeue(out long next) ? next : high ? maxExclusive - 1 : minInclusive;
        }
    }
}
