using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Loot;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Loot;

public class LootCatalogShould
{
    private static LootTableEntry Item(int sequence, ulong item = 1, float chance = 50f, int? group = null,
        int min = 1, int max = 1) => new()
    {
        Sequence = sequence, ItemTemplateId = new ItemTemplateId(item), Chance = chance, GroupId = group,
        MinCount = min, MaxCount = max
    };

    private static LootTableEntry Reference(int sequence, int table, float chance = 100f) => new()
    {
        Sequence = sequence, ReferenceTableId = new LootTableId(table), Chance = chance, MinCount = 1, MaxCount = 1
    };

    private static LootTable Table(int id, string name, params LootTableEntry[] entries)
    {
        foreach (LootTableEntry entry in entries)
            entry.LootTableId = new LootTableId(id);
        return new LootTable { Id = new LootTableId(id), Name = name, Entries = [.. entries] };
    }

    private static LootCatalog Build(params LootTable[] tables) => new(tables, NullLoggerFactory.Instance);

    private static LootTableRefusal RefusalOf(LootCatalog catalog, int id) =>
        Assert.Single(catalog.Refused, r => r.Id == new LootTableId(id));

    [Fact]
    public void Split_A_Table_Into_Ungrouped_Entries_And_Groups_In_Roll_Order()
    {
        LootCatalog catalog = Build(Table(1, "boar",
            Item(5, group: 2), Item(1), Item(4, group: 1), Item(2, group: 2), Item(3)));

        Assert.True(catalog.TryGet(new LootTableId(1), out LootTableView? table));
        Assert.Equal([1, 3], table!.Ungrouped.Select(e => e.Sequence));
        Assert.Equal([1, 2], table.Groups.Select(g => g.GroupId));
        Assert.Equal([2, 5], table.Groups[1].Entries.Select(e => e.Sequence));
        Assert.Empty(catalog.Refused);
        Assert.Equal(1, catalog.TableCount);
        Assert.Equal(5, catalog.EntryCount);
    }

    [Fact]
    public void Refuse_Both_Tables_Of_A_Reference_Cycle_And_Name_Them()
    {
        LootCatalog catalog = Build(Table(1, "alpha", Reference(1, 2)), Table(2, "beta", Reference(1, 1)));

        Assert.False(catalog.TryGet(new LootTableId(1), out _));
        Assert.False(catalog.TryGet(new LootTableId(2), out _));
        Assert.Equal("loot table 1 'alpha': reference cycle 1 -> 2 -> 1", RefusalOf(catalog, 1).ToString());
        Assert.Equal("loot table 2 'beta': reference cycle 2 -> 1 -> 2", RefusalOf(catalog, 2).ToString());
    }

    [Fact]
    public void Refuse_A_Table_That_References_Itself()
    {
        LootCatalog catalog = Build(Table(3, "ouroboros", Reference(1, 3)));

        Assert.Equal("reference cycle 3 -> 3", RefusalOf(catalog, 3).Reason);
    }

    [Fact]
    public void Refuse_A_Reference_To_A_Table_That_Does_Not_Exist()
    {
        LootCatalog catalog = Build(Table(1, "boar", Item(1), Reference(2, 99)));

        Assert.Equal("loot table 1 'boar': entry 2 references missing table 99", RefusalOf(catalog, 1).ToString());
    }

    [Fact]
    public void Refuse_An_Entry_With_Both_Targets()
    {
        LootTableEntry both = Item(1);
        both.ReferenceTableId = new LootTableId(2);

        LootCatalog catalog = Build(Table(1, "boar", both), Table(2, "shared", Item(1)));

        Assert.Equal("entry 1 names both an item and a table", RefusalOf(catalog, 1).Reason);
        Assert.True(catalog.TryGet(new LootTableId(2), out _));
    }

    [Fact]
    public void Refuse_An_Entry_With_Neither_Target()
    {
        LootTableEntry neither = Item(1);
        neither.ItemTemplateId = null;

        Assert.Equal("entry 1 names neither an item nor a table", RefusalOf(Build(Table(1, "boar", neither)), 1).Reason);
    }

    [Theory]
    [InlineData(0, 1, "entry 1 has MinCount 0; it must be at least 1")]
    [InlineData(3, 2, "entry 1 has MinCount 3 above MaxCount 2")]
    [InlineData(1, 1001, "entry 1 has MaxCount 1001; it must be at most 1000")]
    public void Refuse_A_Count_Range_Out_Of_Order_Or_Out_Of_Bounds(int min, int max, string reason)
    {
        Assert.Equal(reason, RefusalOf(Build(Table(1, "boar", Item(1, min: min, max: max))), 1).Reason);
    }

    [Theory]
    [InlineData(-1f, "entry 1 has Chance -1; it must be between 0 and 100")]
    [InlineData(100.5f, "entry 1 has Chance 100.5; it must be between 0 and 100")]
    [InlineData(float.NaN, "entry 1 has Chance NaN; it must be between 0 and 100")]
    public void Refuse_A_Chance_Outside_Zero_To_One_Hundred(float chance, string reason)
    {
        Assert.Equal(reason, RefusalOf(Build(Table(1, "boar", Item(1, chance: chance))), 1).Reason);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(100f)]
    public void Accept_The_Chance_Bounds_Themselves(float chance)
    {
        Assert.Empty(Build(Table(1, "boar", Item(1, chance: chance))).Refused);
    }

    [Fact]
    public void Refuse_A_Table_That_References_A_Refused_Table()
    {
        LootCatalog catalog = Build(
            Table(1, "boar", Reference(1, 2)),
            Table(2, "shared", Item(1, min: 0)));

        Assert.Equal("entry 1 references refused table 2", RefusalOf(catalog, 1).Reason);
        Assert.Equal(0, catalog.TableCount);
    }

    [Fact]
    public void Refuse_A_Table_That_Only_Leads_Into_A_Cycle()
    {
        // 3 is not on the cycle, but rolling it would walk into one.
        LootCatalog catalog = Build(
            Table(1, "alpha", Reference(1, 2)),
            Table(2, "beta", Reference(1, 1)),
            Table(3, "gamma", Reference(1, 1)));

        Assert.Equal("entry 1 references refused table 1", RefusalOf(catalog, 3).Reason);
    }

    [Fact]
    public void Describe_Counts_And_Every_Refusal()
    {
        LootCatalog catalog = Build(Table(1, "boar", Item(1), Item(2)), Table(2, "bad", Item(1, min: 0)));

        Assert.Equal(
            "1 tables, 2 entries, 1 refused (loot table 2 'bad': entry 1 has MinCount 0; it must be at least 1)",
            catalog.Describe());
    }
}
