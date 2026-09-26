using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// Vendor stock is World DB reference data (#432). These run against the real EF model on SQLite,
/// so the keys, the unique index and the check constraints are the shipped ones. Creature
/// template 11 (Marta) and item templates 1 and 2 (Health and Mana Potion) are seeded by the model.
/// Row ids from 900 leave the seeded rows (Task 10) alone.
/// </summary>
public class VendorStockRepositoryShould
{
    private static VendorStock Row(int id, uint sequence, ulong item = 1) => new()
    {
        Id = id, CreatureTemplateId = new CreatureTemplateId(11), Sequence = sequence, ItemTemplateId = new ItemTemplateId(item),
    };

    [Fact]
    public async Task Load_Every_Row_With_Its_Costs()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();

        await using (WorldDbContext write = database.CreateDbContext())
        {
            VendorStock limited = Row(900, 1, item: 2);
            limited.MaxStock = 5;
            limited.RestockSeconds = 600;
            limited.PriceOverride = 25;
            limited.Costs = [new VendorStockCost { ItemTemplateId = new ItemTemplateId(1), Count = 2 }];

            VendorStock gated = Row(901, 2);
            gated.RequiredQuestId = 7;
            gated.RequiredQuestState = QuestRequirementState.Completed;

            write.VendorStocks.AddRange(limited, gated);
            await write.SaveChangesAsync();
        }

        IReadOnlyCollection<VendorStock> rows = await new VendorStockRepository(database).GetAllAsync();

        VendorStock read = rows.Single(r => r.Id == 900);
        Assert.Equal((5u, 600u, 25u), (read.MaxStock!.Value, read.RestockSeconds!.Value, read.PriceOverride!.Value));
        VendorStockCost cost = Assert.Single(read.Costs);
        Assert.Equal((new ItemTemplateId(1), 2u), (cost.ItemTemplateId, cost.Count));

        VendorStock gatedRead = rows.Single(r => r.Id == 901);
        Assert.Equal((7u, QuestRequirementState.Completed), (gatedRead.RequiredQuestId!.Value, gatedRead.RequiredQuestState!.Value));
        Assert.Empty(gatedRead.Costs);
    }

    [Fact]
    public async Task Refuse_A_Limited_Row_With_No_Restock_Timer()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext write = database.CreateDbContext();

        VendorStock row = Row(900, 1);
        row.MaxStock = 2;
        write.VendorStocks.Add(row);

        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
    }

    [Fact]
    public async Task Refuse_A_Restock_Timer_On_An_Unlimited_Row()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext write = database.CreateDbContext();

        VendorStock row = Row(900, 1);
        row.RestockSeconds = 60;
        write.VendorStocks.Add(row);

        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
    }

    [Fact]
    public async Task Refuse_A_Max_Stock_Of_Zero()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext write = database.CreateDbContext();

        VendorStock row = Row(900, 1);
        row.MaxStock = 0;
        row.RestockSeconds = 60;
        write.VendorStocks.Add(row);

        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
    }

    [Fact]
    public async Task Refuse_A_Quest_Id_Without_A_State()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext write = database.CreateDbContext();

        VendorStock row = Row(900, 1);
        row.RequiredQuestId = 7;
        write.VendorStocks.Add(row);

        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
    }

    [Fact]
    public async Task Refuse_A_Cost_Count_Of_Zero()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext write = database.CreateDbContext();

        VendorStock row = Row(900, 1, item: 2);
        row.Costs = [new VendorStockCost { ItemTemplateId = new ItemTemplateId(1), Count = 0 }];
        write.VendorStocks.Add(row);

        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
    }

    [Fact]
    public async Task Refuse_Two_Rows_With_One_Sequence_For_One_Vendor()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        await using WorldDbContext write = database.CreateDbContext();

        write.VendorStocks.AddRange(Row(900, 1), Row(901, 1, item: 2));

        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
    }

    [Theory]
    [InlineData(QuestRequirementState.Active, 0)]
    [InlineData(QuestRequirementState.Completed, 1)]
    public void Store_The_Quest_State_As_Its_Number(QuestRequirementState state, int stored)
    {
        Assert.Equal(stored, (int)state);
    }
}
