using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// Loot tables are World DB reference data (issue #460). These run against the real EF model on
/// SQLite, so the composite key, the foreign keys and the check constraint are the shipped ones.
/// </summary>
public class LootTableRepositoryShould
{
    [Fact]
    public async Task Load_Every_Table_With_Its_Entries()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();

        await using (WorldDbContext write = database.CreateDbContext())
        {
            write.LootTables.Add(new LootTable { Id = 900, Name = "shared" });
            write.LootTables.Add(new LootTable
            {
                Id = 901,
                Name = "boar",
                Entries =
                [
                    // Item template 1 is the seeded Health Potion.
                    new LootTableEntry { Sequence = 1, ItemTemplateId = new ItemTemplateId(1), Chance = 25f, MinCount = 1, MaxCount = 2 },
                    new LootTableEntry { Sequence = 2, ReferenceTableId = new LootTableId(900), Chance = 50f, MinCount = 1, MaxCount = 1 },
                ]
            });
            await write.SaveChangesAsync();
        }

        IReadOnlyCollection<LootTable> tables = await new LootTableRepository(database).GetAllAsync();

        LootTable boar = tables.Single(t => t.Id == new LootTableId(901));
        Assert.Equal([1, 2], boar.Entries.OrderBy(e => e.Sequence).Select(e => e.Sequence));
        Assert.Equal(new ItemTemplateId(1), boar.Entries.Single(e => e.Sequence == 1).ItemTemplateId);
        Assert.Equal(new LootTableId(900), boar.Entries.Single(e => e.Sequence == 2).ReferenceTableId);
        Assert.Empty(tables.Single(t => t.Id == new LootTableId(900)).Entries);
    }

    [Fact]
    public async Task Refuse_An_Entry_That_Names_Both_An_Item_And_A_Table()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext write = database.CreateDbContext();

        write.LootTables.Add(new LootTable { Id = 900, Name = "shared" });
        write.LootTables.Add(new LootTable
        {
            Id = 901,
            Name = "both",
            Entries =
            [
                new LootTableEntry
                {
                    Sequence = 1, ItemTemplateId = new ItemTemplateId(1), ReferenceTableId = new LootTableId(900),
                    Chance = 10f, MinCount = 1, MaxCount = 1
                }
            ]
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
    }

    [Fact]
    public async Task Refuse_An_Entry_That_Names_Neither_An_Item_Nor_A_Table()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext write = database.CreateDbContext();

        write.LootTables.Add(new LootTable
        {
            Id = 901,
            Name = "neither",
            Entries = [new LootTableEntry { Sequence = 1, Chance = 10f, MinCount = 1, MaxCount = 1 }]
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
    }
}
