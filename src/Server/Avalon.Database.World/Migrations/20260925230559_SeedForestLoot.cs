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
                    { 5m, "Wizard", 200L, 1, 5L, null, 2L, null, 0, null, 5L, 256, 3, 1L, "Thornwood Staff", 2, 1, 50L, 9, 12, null, 3, null, null, null, null, null, null, null, 18L, null, 1L, null, null, null, null, null, null, null, 101 },
                    { 6m, "Hunter", 200L, 1, 4L, null, 2L, null, 0, null, 6L, 256, 3, 1L, "Briarstring Bow", 2, 1, 50L, 9, 12, null, 2, null, null, null, null, null, null, null, 15L, null, 1L, null, null, null, null, null, null, null, 102 },
                    { 7m, "Warrior", 200L, 1, 4L, null, 2L, null, 0, null, 7L, 256, 3, 1L, "Bramblesteel Sword", 2, 1, 50L, 9, 12, null, 1, null, null, null, null, null, null, null, 13L, null, 1L, null, null, null, null, null, null, null, 100 },
                    { 8m, "Healer", 200L, 1, 4L, null, 2L, null, 0, null, 8L, 256, 3, 1L, "Rootknot Mace", 2, 1, 50L, 9, 12, null, 3, null, null, null, null, null, null, null, 15L, null, 1L, null, null, null, null, null, null, null, 100 },
                    { 9m, "Warrior,Wizard,Hunter,Healer", 20L, 0, null, null, null, null, null, null, 9L, 256, null, 20L, "Scroll of Falling Leaves", 1, null, 5L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 2 },
                    { 10m, "Warrior,Wizard,Hunter,Healer", 20L, 0, null, null, null, null, null, null, 10L, 256, null, 20L, "Scroll of the Mossy Hollow", 1, null, 5L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 2 },
                    { 11m, "Warrior,Wizard,Hunter,Healer", 20L, 0, null, null, null, null, null, null, 11L, 256, null, 20L, "Scroll of Whispering Pines", 1, null, 5L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 2 },
                    { 12m, "Warrior", 200L, 2, null, null, null, null, null, null, 12L, 256, 3, 1L, "Barkplate Helm", 2, 1, 50L, 0, 1, null, 4, 0, null, null, null, null, null, null, 1L, null, 4L, 1L, null, null, null, null, null, null, 201 },
                    { 13m, "Warrior", 200L, 2, null, null, null, null, null, null, 13L, 256, 3, 1L, "Barkplate Chestguard", 2, 1, 50L, 3, 1, null, 4, 0, null, null, null, null, null, null, 2L, null, 8L, 2L, null, null, null, null, null, null, 202 },
                    { 14m, "Warrior", 200L, 2, null, null, null, null, null, null, 14L, 256, 3, 1L, "Barkplate Legguards", 2, 1, 50L, 5, 1, null, 4, 0, null, null, null, null, null, null, 2L, null, 6L, 1L, null, null, null, null, null, null, 203 },
                    { 15m, "Warrior", 200L, 2, null, null, null, null, null, null, 15L, 256, 3, 1L, "Barkplate Gauntlets", 2, 1, 50L, 4, 1, null, 4, 0, null, null, null, null, null, null, 1L, null, 3L, 1L, null, null, null, null, null, null, 205 },
                    { 16m, "Warrior", 200L, 2, null, null, null, null, null, null, 16L, 256, 3, 1L, "Barkplate Boots", 2, 1, 50L, 6, 1, null, 4, 0, null, null, null, null, null, null, 1L, null, 3L, 1L, null, null, null, null, null, null, 204 },
                    { 17m, "Wizard", 200L, 2, null, null, null, null, null, null, 17L, 256, 3, 1L, "Mossweave Hood", 2, 1, 50L, 0, 3, null, null, null, null, null, null, null, null, null, 2L, null, null, null, null, null, null, null, null, null, 201 },
                    { 18m, "Wizard", 200L, 2, null, null, null, null, null, null, 18L, 256, 3, 1L, "Mossweave Robe", 2, 1, 50L, 3, 3, null, null, null, null, null, null, null, null, null, 3L, null, null, null, null, null, null, null, null, null, 202 },
                    { 19m, "Wizard", 200L, 2, null, null, null, null, null, null, 19L, 256, 3, 1L, "Mossweave Leggings", 2, 1, 50L, 5, 3, null, null, null, null, null, null, null, null, null, 3L, null, null, null, null, null, null, null, null, null, 203 },
                    { 20m, "Wizard", 200L, 2, null, null, null, null, null, null, 20L, 256, 3, 1L, "Mossweave Gloves", 2, 1, 50L, 4, 3, null, null, null, null, null, null, null, null, null, 1L, null, null, null, null, null, null, null, null, null, 205 },
                    { 21m, "Wizard", 200L, 2, null, null, null, null, null, null, 21L, 256, 3, 1L, "Mossweave Slippers", 2, 1, 50L, 6, 3, null, null, null, null, null, null, null, null, null, 1L, null, null, null, null, null, null, null, null, null, 204 },
                    { 22m, "Hunter", 200L, 2, null, null, null, null, null, null, 22L, 256, 3, 1L, "Fernstalker Cap", 2, 1, 50L, 0, 2, null, null, null, null, null, null, null, null, null, 2L, null, null, null, null, null, null, null, null, null, 201 },
                    { 23m, "Hunter", 200L, 2, null, null, null, null, null, null, 23L, 256, 3, 1L, "Fernstalker Jerkin", 2, 1, 50L, 3, 2, null, null, null, null, null, null, null, null, null, 3L, null, null, null, null, null, null, null, null, null, 202 },
                    { 24m, "Hunter", 200L, 2, null, null, null, null, null, null, 24L, 256, 3, 1L, "Fernstalker Breeches", 2, 1, 50L, 5, 2, null, null, null, null, null, null, null, null, null, 3L, null, null, null, null, null, null, null, null, null, 203 },
                    { 25m, "Hunter", 200L, 2, null, null, null, null, null, null, 25L, 256, 3, 1L, "Fernstalker Grips", 2, 1, 50L, 4, 2, null, null, null, null, null, null, null, null, null, 1L, null, null, null, null, null, null, null, null, null, 205 },
                    { 26m, "Hunter", 200L, 2, null, null, null, null, null, null, 26L, 256, 3, 1L, "Fernstalker Boots", 2, 1, 50L, 6, 2, null, null, null, null, null, null, null, null, null, 1L, null, null, null, null, null, null, null, null, null, 204 },
                    { 27m, "Healer", 200L, 2, null, null, null, null, null, null, 27L, 256, 3, 1L, "Dewleaf Circlet", 2, 1, 50L, 0, 3, null, 0, null, null, null, null, null, null, null, 1L, null, 1L, null, null, null, null, null, null, null, 201 },
                    { 28m, "Healer", 200L, 2, null, null, null, null, null, null, 28L, 256, 3, 1L, "Dewleaf Vestments", 2, 1, 50L, 3, 3, null, 0, null, null, null, null, null, null, null, 2L, null, 2L, null, null, null, null, null, null, null, 202 },
                    { 29m, "Healer", 200L, 2, null, null, null, null, null, null, 29L, 256, 3, 1L, "Dewleaf Leggings", 2, 1, 50L, 5, 3, null, 0, null, null, null, null, null, null, null, 2L, null, 1L, null, null, null, null, null, null, null, 203 },
                    { 30m, "Healer", 200L, 2, null, null, null, null, null, null, 30L, 256, 3, 1L, "Dewleaf Handwraps", 2, 1, 50L, 4, 3, null, 0, null, null, null, null, null, null, null, 1L, null, 1L, null, null, null, null, null, null, null, 205 },
                    { 31m, "Healer", 200L, 2, null, null, null, null, null, null, 31L, 256, 3, 1L, "Dewleaf Sandals", 2, 1, 50L, 6, 3, null, 0, null, null, null, null, null, null, null, 1L, null, 1L, null, null, null, null, null, null, null, 204 }
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
                    { 9, "Forest weapons" },
                    { 10, "Forest scrolls" },
                    { 11, "Forest armour" }
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
                    { 2, 4, 2f, null, null, 1, 1, 9 },
                    { 2, 5, 10f, null, null, 1, 1, 10 },
                    { 2, 6, 100f, null, null, 1, 1, 11 },
                    { 3, 1, 20f, null, 1m, 1, 1, null },
                    { 3, 2, 10f, null, 2m, 1, 1, null },
                    { 3, 3, 25f, null, null, 1, 1, 1 },
                    { 3, 4, 2f, null, null, 1, 1, 9 },
                    { 3, 5, 10f, null, null, 1, 1, 10 },
                    { 3, 6, 100f, null, null, 1, 1, 11 },
                    { 4, 1, 20f, null, 1m, 1, 1, null },
                    { 4, 2, 10f, null, 2m, 1, 1, null },
                    { 4, 3, 25f, null, null, 1, 1, 1 },
                    { 4, 4, 2f, null, null, 1, 1, 9 },
                    { 4, 5, 10f, null, null, 1, 1, 10 },
                    { 4, 6, 100f, null, null, 1, 1, 11 },
                    { 5, 1, 20f, null, 1m, 1, 1, null },
                    { 5, 2, 10f, null, 2m, 1, 1, null },
                    { 5, 3, 25f, null, null, 1, 1, 1 },
                    { 5, 4, 2f, null, null, 1, 1, 9 },
                    { 5, 5, 10f, null, null, 1, 1, 10 },
                    { 5, 6, 100f, null, null, 1, 1, 11 },
                    { 6, 1, 40f, null, 1m, 2, 1, null },
                    { 6, 2, 25f, null, 2m, 1, 1, null },
                    { 6, 3, 50f, null, null, 1, 1, 1 },
                    { 6, 4, 2f, null, null, 1, 1, 9 },
                    { 6, 5, 10f, null, null, 1, 1, 10 },
                    { 6, 6, 100f, null, null, 1, 1, 11 },
                    { 7, 1, 60f, null, 1m, 3, 1, null },
                    { 7, 2, 40f, null, 2m, 2, 1, null },
                    { 7, 3, 75f, null, null, 1, 1, 1 },
                    { 7, 4, 2f, null, null, 1, 1, 9 },
                    { 7, 5, 10f, null, null, 1, 1, 10 },
                    { 7, 6, 100f, null, null, 1, 1, 11 },
                    { 8, 1, 100f, null, 1m, 4, 2, null },
                    { 8, 2, 100f, null, 2m, 3, 1, null },
                    { 8, 3, 100f, null, null, 1, 1, 1 },
                    { 8, 4, 2f, null, null, 1, 1, 9 },
                    { 8, 5, 10f, null, null, 1, 1, 10 },
                    { 8, 6, 100f, null, null, 1, 1, 11 },
                    { 9, 1, 25f, 1, 7m, 1, 1, null },
                    { 9, 2, 25f, 1, 5m, 1, 1, null },
                    { 9, 3, 25f, 1, 6m, 1, 1, null },
                    { 9, 4, 25f, 1, 8m, 1, 1, null },
                    { 10, 1, 33f, 1, 9m, 1, 1, null },
                    { 10, 2, 33f, 1, 10m, 1, 1, null },
                    { 10, 3, 33f, 1, 11m, 1, 1, null },
                    { 11, 1, 2f, null, 12m, 1, 1, null },
                    { 11, 2, 2f, null, 13m, 1, 1, null },
                    { 11, 3, 2f, null, 14m, 1, 1, null },
                    { 11, 4, 2f, null, 15m, 1, 1, null },
                    { 11, 5, 2f, null, 16m, 1, 1, null },
                    { 11, 6, 2f, null, 17m, 1, 1, null },
                    { 11, 7, 2f, null, 18m, 1, 1, null },
                    { 11, 8, 2f, null, 19m, 1, 1, null },
                    { 11, 9, 2f, null, 20m, 1, 1, null },
                    { 11, 10, 2f, null, 21m, 1, 1, null },
                    { 11, 11, 2f, null, 22m, 1, 1, null },
                    { 11, 12, 2f, null, 23m, 1, 1, null },
                    { 11, 13, 2f, null, 24m, 1, 1, null },
                    { 11, 14, 2f, null, 25m, 1, 1, null },
                    { 11, 15, 2f, null, 26m, 1, 1, null },
                    { 11, 16, 2f, null, 27m, 1, 1, null },
                    { 11, 17, 2f, null, 28m, 1, 1, null },
                    { 11, 18, 2f, null, 29m, 1, 1, null },
                    { 11, 19, 2f, null, 30m, 1, 1, null },
                    { 11, 20, 2f, null, 31m, 1, 1, null }
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
                keyValues: new object[] { 2, 5 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 2, 6 });

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
                keyValues: new object[] { 3, 5 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 3, 6 });

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
                keyValues: new object[] { 4, 5 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 4, 6 });

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
                keyValues: new object[] { 5, 5 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 5, 6 });

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
                keyValues: new object[] { 6, 5 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 6, 6 });

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
                keyValues: new object[] { 7, 5 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 7, 6 });

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
                keyValues: new object[] { 8, 5 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 8, 6 });

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
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 9, 4 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 10, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 10, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 10, 3 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 3 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 4 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 5 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 6 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 7 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 8 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 9 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 10 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 11 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 12 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 13 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 14 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 15 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 16 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 17 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 18 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 19 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 11, 20 });

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 9m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 10m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 11m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 12m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 13m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 14m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 15m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 16m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 17m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 18m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 19m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 20m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 21m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 22m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 23m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 24m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 25m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 26m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 27m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 28m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 29m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 30m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 31m);

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

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 11);

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
