using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedForestLoot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ItemTemplates",
                columns: new[] { "Id", "AllowedClasses", "BuyPrice", "Class", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2", "DisplayId", "Flags", "ItemPower", "MaxStackSize", "Name", "Rarity", "RequiredLevel", "SellPrice", "Slot", "StatType1", "StatType10", "StatType2", "StatType3", "StatType4", "StatType5", "StatType6", "StatType7", "StatType8", "StatType9", "StatValue1", "StatValue10", "StatValue2", "StatValue3", "StatValue4", "StatValue5", "StatValue6", "StatValue7", "StatValue8", "StatValue9", "SubClass" },
                values: new object[,]
                {
                    { 5m, "Wizard,Healer", 100L, 1, 3L, null, 1L, null, 0, null, 5L, 256, 2, 1L, "Splintered Staff", 1, 1, 50L, 9, 12, null, null, null, null, null, null, null, null, null, 18L, null, null, null, null, null, null, null, null, null, 101 },
                    { 6m, "Hunter", 100L, 1, 3L, null, 1L, null, 0, null, 6L, 256, 2, 1L, "Warped Shortbow", 1, 1, 50L, 9, 12, null, null, null, null, null, null, null, null, null, 15L, null, null, null, null, null, null, null, null, null, 102 }
                });

            migrationBuilder.InsertData(
                table: "LootTables",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Forest common" },
                    { 2, "Thornback Boar" },
                    { 3, "Grey Fen Wolf" },
                    { 4, "Blightfly Swarmling" },
                    { 5, "Husk of the Wold" },
                    { 6, "Bramblemaw Alpha" },
                    { 7, "Old Tuskroot" },
                    { 8, "Mother Bramble" },
                    { 9, "Forest weapons" }
                });

            migrationBuilder.InsertData(
                table: "LootTableEntries",
                columns: new[] { "LootTableId", "Sequence", "Chance", "GroupId", "ItemTemplateId", "MaxCount", "MinCount", "ReferenceTableId" },
                values: new object[,]
                {
                    { 1, 1, 5f, null, 3m, 1, 1, null },
                    { 1, 2, 10f, null, 1m, 2, 1, null },
                    { 2, 1, 20f, null, 1m, 1, 1, null },
                    { 2, 2, 10f, null, 2m, 1, 1, null },
                    { 2, 3, 25f, null, null, 1, 1, 1 },
                    { 2, 4, 10f, null, null, 1, 1, 9 },
                    { 3, 1, 20f, null, 1m, 1, 1, null },
                    { 3, 2, 10f, null, 2m, 1, 1, null },
                    { 3, 3, 25f, null, null, 1, 1, 1 },
                    { 3, 4, 10f, null, null, 1, 1, 9 },
                    { 4, 1, 20f, null, 1m, 1, 1, null },
                    { 4, 2, 10f, null, 2m, 1, 1, null },
                    { 4, 3, 25f, null, null, 1, 1, 1 },
                    { 4, 4, 10f, null, null, 1, 1, 9 },
                    { 5, 1, 20f, null, 1m, 1, 1, null },
                    { 5, 2, 10f, null, 2m, 1, 1, null },
                    { 5, 3, 25f, null, null, 1, 1, 1 },
                    { 5, 4, 10f, null, null, 1, 1, 9 },
                    { 6, 1, 40f, null, 1m, 2, 1, null },
                    { 6, 2, 25f, null, 2m, 1, 1, null },
                    { 6, 3, 50f, null, null, 1, 1, 1 },
                    { 6, 4, 10f, null, null, 1, 1, 9 },
                    { 7, 1, 60f, null, 1m, 3, 1, null },
                    { 7, 2, 40f, null, 2m, 2, 1, null },
                    { 7, 3, 75f, null, null, 1, 1, 1 },
                    { 7, 4, 10f, null, null, 1, 1, 9 },
                    { 8, 1, 100f, null, 1m, 4, 2, null },
                    { 8, 2, 100f, null, 2m, 3, 1, null },
                    { 8, 3, 100f, null, null, 1, 1, 1 },
                    { 8, 4, 10f, null, null, 1, 1, 9 },
                    { 9, 1, 34f, 1, 4m, 1, 1, null },
                    { 9, 2, 33f, 1, 5m, 1, 1, null },
                    { 9, 3, 33f, 1, 6m, 1, 1, null }
                });

            // The creature rows name the loot tables, so they are updated only once the tables exist:
            // the foreign key refuses a LootTableId that names no row.
            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { 2, 8, 3 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { 3, 10, 4 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { 4, 4, 1 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { 5, 14, 6 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { 6, 45, 20 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { 7, 90, 40 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { 8, 300, 150 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 1, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 1, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 2, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 2, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 2, 3 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 2, 4 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 3, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 3, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 3, 3 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 3, 4 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 4, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 4, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 4, 3 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 4, 4 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 5, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 5, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 5, 3 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 5, 4 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 6, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 6, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 6, 3 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 6, 4 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 7, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 7, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 7, 3 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 7, 4 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 8, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 8, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 8, 3 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 8, 4 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 3 });

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { null, 0, 0 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { null, 0, 0 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { null, 0, 0 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { null, 0, 0 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { null, 0, 0 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { null, 0, 0 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                columns: new[] { "LootTableId", "MaxGold", "MinGold" },
                values: new object[] { null, 0, 0 });
        }
    }
}
