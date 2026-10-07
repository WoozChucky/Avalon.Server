using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Loot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Loot;

public class LootCatalogShould
{
    private static LootTableEntry Item(int sequence, ulong item = 1, float chance = 50f, int? group = null,
        int min = 1, int max = 1) => new()
        {
            Sequence = sequence,
            ItemTemplateId = new ItemTemplateId(item),
            Chance = chance,
            GroupId = group,
            MinCount = min,
            MaxCount = max
        };

    private static LootTableEntry Reference(int sequence, int table, float chance = 100f) => new()
    {
        Sequence = sequence,
        ReferenceTableId = new LootTableId(table),
        Chance = chance,
        MinCount = 1,
        MaxCount = 1
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

    /// <summary>An entry that breaks any rule refuses its own table, and only that table.</summary>
    [Theory]
    [InlineData(true, true, 50f, 1, 1, "entry 1 names both an item and a table")]
    [InlineData(false, false, 50f, 1, 1, "entry 1 names neither an item nor a table")]
    [InlineData(true, false, 50f, 0, 1, "entry 1 has MinCount 0; it must be at least 1")]
    [InlineData(true, false, 50f, 3, 2, "entry 1 has MinCount 3 above MaxCount 2")]
    [InlineData(true, false, 50f, 1, 1001, "entry 1 has MaxCount 1001; it must be at most 1000")]
    [InlineData(true, false, -1f, 1, 1, "entry 1 has Chance -1; it must be between 0 and 100")]
    [InlineData(true, false, 100.5f, 1, 1, "entry 1 has Chance 100.5; it must be between 0 and 100")]
    [InlineData(true, false, float.NaN, 1, 1, "entry 1 has Chance NaN; it must be between 0 and 100")]
    public void Refuse_A_Table_With_A_Bad_Entry_And_Keep_The_Others(
        bool namesItem, bool namesTable, float chance, int min, int max, string reason)
    {
        LootTableEntry entry = Item(1, chance: chance, min: min, max: max);
        if (!namesItem)
            entry.ItemTemplateId = null;
        if (namesTable)
            entry.ReferenceTableId = new LootTableId(2);

        LootCatalog catalog = Build(Table(1, "boar", entry), Table(2, "shared", Item(1)));

        Assert.Equal(reason, RefusalOf(catalog, 1).Reason);
        Assert.True(catalog.TryGet(new LootTableId(2), out _));
    }

    [Theory]
    [InlineData(0f, 1)]
    [InlineData(100f, 1)]
    [InlineData(50f, LootCatalog.MaxEntryCount)]
    public void Accept_An_Entry_At_Each_Bound(float chance, int max)
    {
        LootCatalog catalog = Build(Table(1, "boar", Item(1, chance: chance, max: max)));

        Assert.Empty(catalog.Refused);
        Assert.True(catalog.TryGet(new LootTableId(1), out _));
    }

    [Fact]
    public void Refuse_A_Referrer_Whose_Refused_Target_Is_Only_Found_On_A_Later_Pass()
    {
        // The referrer has the lowest id, so the first pass in id order sees table 2 still accepted;
        // only the pass after table 2 is refused catches table 1.
        LootCatalog catalog = Build(
            Table(1, "boar", Reference(1, 2)),
            Table(2, "shared", Reference(1, 3)),
            Table(3, "bad", Item(1, min: 0)));

        Assert.Equal("entry 1 references refused table 2", RefusalOf(catalog, 1).Reason);
        Assert.Equal("entry 1 references refused table 3", RefusalOf(catalog, 2).Reason);
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
}
