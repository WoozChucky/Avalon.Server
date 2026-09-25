using Avalon.Domain.World;
using Avalon.World.Loot;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Avalon.Server.World.UnitTests.Loot.LootTestData;

namespace Avalon.Server.World.UnitTests.Loot;

public class LootRollerShould
{
    private static LootRoller Seeded(int seed = 460) =>
        new(new LootRandom(new Random(seed)), NullLogger<LootRoller>.Instance);

    private static LootRoller Scripted(double[] doubles, long[]? integers = null) =>
        new(new ScriptedLootRandom(doubles, integers), NullLogger<LootRoller>.Instance);

    [Fact]
    public void Hit_An_Independent_Entry_About_As_Often_As_Its_Chance()
    {
        LootCatalog catalog = Catalog(Table(1, Item(1, Potion, chance: 25f)));
        LootRoller roller = Seeded();

        int hits = 0;
        for (int i = 0; i < 10_000; i++)
            hits += roller.Roll(BoarTemplate(1), catalog, Items).Count;

        Assert.InRange(hits, 2_300, 2_700);
    }

    [Fact]
    public void Roll_Each_Independent_Entry_On_Its_Own()
    {
        // 0.10 * 100 < 50 hits; 0.60 * 100 < 50 misses; 0.20 * 100 < 30 hits.
        LootCatalog catalog = Catalog(Table(1,
            Item(1, Potion, chance: 50f), Item(2, Sword, chance: 50f), Item(3, Staff, chance: 30f)));

        IReadOnlyList<RolledDrop> drops = Scripted([0.10, 0.60, 0.20], [1, 1]).Roll(BoarTemplate(1), catalog, Items);

        Assert.Equal([Potion.Id, Staff.Id], drops.Select(d => d.ItemTemplateId!));
    }

    [Theory]
    [InlineData(0f, 0.0, 0)]
    [InlineData(100f, 0.999999, 1)]
    public void Treat_Chance_Zero_As_Never_And_One_Hundred_As_Always(float chance, double roll, int expected)
    {
        LootCatalog catalog = Catalog(Table(1, Item(1, Sword, chance: chance)));

        Assert.Equal(expected, Scripted([roll], [1]).Roll(BoarTemplate(1), catalog, Items).Count);
    }

    [Fact]
    public void Drop_Exactly_One_Entry_Of_A_Group_Picked_By_Weight()
    {
        LootCatalog catalog = Catalog(Table(1,
            Item(1, Sword, chance: 75f, group: 1), Item(2, Staff, chance: 25f, group: 1)));
        LootRoller roller = Seeded();

        int swords = 0;
        for (int i = 0; i < 10_000; i++)
        {
            RolledDrop drop = Assert.Single(roller.Roll(BoarTemplate(1), catalog, Items));
            if (drop.ItemTemplateId == Sword.Id)
                swords++;
        }

        Assert.InRange(swords, 7_200, 7_800);
    }

    [Theory]
    [InlineData(0.00, 200UL)]   // 0 of 100: the sword's 0-75 band
    [InlineData(0.74, 200UL)]
    [InlineData(0.75, 201UL)]   // 75 of 100: the staff's 75-100 band
    [InlineData(0.99, 201UL)]
    public void Pick_A_Group_Entry_By_Where_The_Roll_Lands_In_The_Weights(double roll, ulong expected)
    {
        LootCatalog catalog = Catalog(Table(1,
            Item(1, Sword, chance: 75f, group: 1), Item(2, Staff, chance: 25f, group: 1)));

        RolledDrop drop = Assert.Single(Scripted([roll], [1]).Roll(BoarTemplate(1), catalog, Items));

        Assert.Equal(expected, drop.ItemTemplateId!.Value);
    }

    [Fact]
    public void Drop_Nothing_From_A_Group_Whose_Weights_Add_Up_To_Zero()
    {
        LootCatalog catalog = Catalog(Table(1,
            Item(1, Sword, chance: 0f, group: 1), Item(2, Staff, chance: 0f, group: 1)));

        // No scripted numbers at all: a zero-weight group must not even draw.
        Assert.Empty(Scripted([]).Roll(BoarTemplate(1), catalog, Items));
    }

    [Fact]
    public void Roll_A_Referenced_Table_When_The_Reference_Hits()
    {
        LootCatalog catalog = Catalog(
            Table(1, Reference(1, table: 2, chance: 50f)),
            Table(2, Item(1, Potion, chance: 100f)));

        // 0.10 hits the reference, 0.50 hits the potion inside table 2, 1 is its count.
        RolledDrop drop = Assert.Single(Scripted([0.10, 0.50], [1]).Roll(BoarTemplate(1), catalog, Items));

        Assert.Equal(Potion.Id, drop.ItemTemplateId);
    }

    [Fact]
    public void Skip_A_Referenced_Table_When_The_Reference_Misses()
    {
        LootCatalog catalog = Catalog(
            Table(1, Reference(1, table: 2, chance: 50f)),
            Table(2, Item(1, Potion, chance: 100f)));

        Assert.Empty(Scripted([0.90]).Roll(BoarTemplate(1), catalog, Items));
    }

    [Fact]
    public void Stop_Following_References_At_The_Depth_Limit()
    {
        // Twelve tables in a chain, each dropping one sword and referencing the next. Only the first
        // MaxReferenceDepth of them, depths 0 to 7, are rolled.
        var tables = new List<LootTable>();
        for (int id = 1; id <= 12; id++)
        {
            tables.Add(id < 12
                ? Table(id, Item(1, Sword), Reference(2, table: id + 1))
                : Table(id, Item(1, Sword)));
        }

        IReadOnlyList<RolledDrop> drops = Seeded().Roll(BoarTemplate(1), Catalog([.. tables]), Items);

        Assert.Equal(LootRoller.MaxReferenceDepth, drops.Count);
    }

    [Fact]
    public void Roll_Counts_Within_The_Entry_Range()
    {
        LootCatalog catalog = Catalog(Table(1, Item(1, Potion, min: 2, max: 5)));
        LootRoller roller = Seeded();

        var seen = new HashSet<uint>();
        for (int i = 0; i < 1_000; i++)
            seen.Add(Assert.Single(roller.Roll(BoarTemplate(1), catalog, Items)).Count);

        Assert.Equal([2u, 3u, 4u, 5u], seen.Order());
    }

    [Fact]
    public void Split_A_Count_Above_The_Stack_Size_Into_Several_Drops()
    {
        LootCatalog catalog = Catalog(Table(1, Item(1, Potion, min: 45, max: 45)));

        IReadOnlyList<RolledDrop> drops = Scripted([0.0], [45]).Roll(BoarTemplate(1), catalog, Items);

        Assert.Equal([20u, 20u, 5u], drops.Select(d => d.Count));
        Assert.All(drops, d => Assert.Equal(Potion.Id, d.ItemTemplateId));
    }

    [Fact]
    public void Treat_A_Stack_Size_Of_Zero_As_One()
    {
        LootCatalog catalog = Catalog(Table(1, Item(1, Pebble, min: 3, max: 3)));

        Assert.Equal([1u, 1u, 1u], Scripted([0.0], [3]).Roll(BoarTemplate(1), catalog, Items).Select(d => d.Count));
    }

    [Fact]
    public void Skip_An_Entry_Whose_Item_Template_No_Longer_Exists_And_Keep_The_Rest()
    {
        LootTableEntry ghost = Item(1, Sword);
        ghost.ItemTemplateId = new Avalon.Common.ValueObjects.ItemTemplateId(999);
        LootCatalog catalog = Catalog(Table(1, ghost, Item(2, Potion)));

        RolledDrop drop = Assert.Single(Scripted([0.0, 0.0], [1]).Roll(BoarTemplate(1), catalog, Items));

        Assert.Equal(Potion.Id, drop.ItemTemplateId);
    }

    [Fact]
    public void Roll_Gold_Within_The_Creature_Range_As_One_Last_Pile()
    {
        LootCatalog catalog = Catalog(Table(1, Item(1, Sword)));
        LootRoller roller = Seeded();

        for (int i = 0; i < 1_000; i++)
        {
            IReadOnlyList<RolledDrop> drops = roller.Roll(BoarTemplate(1, minGold: 5, maxGold: 12), catalog, Items);

            Assert.Equal(2, drops.Count);
            Assert.True(drops[^1].IsGold);
            Assert.InRange(drops[^1].Gold, 5UL, 12UL);
            Assert.Equal(1, drops.Count(d => d.IsGold));
        }
    }

    [Fact]
    public void Skip_Gold_When_Both_Bounds_Are_Zero()
    {
        // No scripted integers: a creature with no gold must not even draw.
        Assert.Empty(Scripted([]).Roll(BoarTemplate(null), Catalog(), Items));
    }

    [Fact]
    public void Skip_Gold_When_The_Roll_Comes_Out_Zero()
    {
        Assert.Empty(Scripted([], [0]).Roll(BoarTemplate(null, minGold: 0, maxGold: 10), Catalog(), Items));
    }

    [Theory]
    [InlineData(-5, 3, 0L, 4L)]    // a negative minimum counts as 0
    [InlineData(9, 4, 9L, 10L)]    // a minimum above the maximum collapses to the minimum
    public void Clamp_A_Mis_Authored_Gold_Range(int min, int max, long low, long highExclusive)
    {
        var random = new ScriptedLootRandom([], [low]);

        new LootRoller(random, NullLogger<LootRoller>.Instance).Roll(BoarTemplate(null, min, max), Catalog(), Items);

        Assert.Equal((low, highExclusive), random.LastIntegerRange);
    }

    [Fact]
    public void Drop_Only_Gold_For_A_Creature_Whose_Table_Was_Refused_Or_Is_Missing()
    {
        IReadOnlyList<RolledDrop> drops = Scripted([], [7]).Roll(BoarTemplate(42, minGold: 7, maxGold: 7), Catalog(), Items);

        Assert.Equal(RolledDrop.GoldPile(7), Assert.Single(drops));
    }

    [Fact]
    public void Drop_Nothing_For_A_Creature_With_No_Table_And_No_Gold()
    {
        Assert.Empty(Seeded().Roll(BoarTemplate(null), Catalog(), Items));
    }
}
