using Avalon.Database.World.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// The hand-written step of AddLootTables. SQLite builds the model with EnsureCreated and never runs
/// this migration, so its operations are read directly.
/// </summary>
public class AddLootTablesMigrationShould
{
    [Fact]
    public void Clear_Every_Loot_Table_Id_Before_Adding_The_Foreign_Key()
    {
        List<MigrationOperation> up = [.. new AddLootTables().UpOperations];

        int foreignKey = up.FindIndex(o =>
            o is AddForeignKeyOperation { Name: "FK_CreatureTemplates_LootTables_LootTableId" });
        int clear = up.FindIndex(o =>
            o is SqlOperation { Sql: "UPDATE \"CreatureTemplates\" SET \"LootTableId\" = NULL;" });

        // Unconditional: a hand-edited non-zero value would otherwise make the key refuse its row.
        Assert.True(clear >= 0, "no unconditional UPDATE clearing LootTableId");
        Assert.True(foreignKey > clear, "the foreign key must be added after LootTableId is cleared");
    }
}
