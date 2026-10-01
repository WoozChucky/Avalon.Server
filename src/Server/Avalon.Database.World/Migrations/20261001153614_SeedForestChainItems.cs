using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedForestChainItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ItemTemplates",
                columns: new[] { "Id", "AllowedClasses", "BuyPrice", "Class", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2", "DisplayId", "Flags", "ItemPower", "MaxStackSize", "Name", "Rarity", "RequiredLevel", "SellPrice", "Slot", "StatType1", "StatType10", "StatType2", "StatType3", "StatType4", "StatType5", "StatType6", "StatType7", "StatType8", "StatType9", "StatValue1", "StatValue10", "StatValue2", "StatValue3", "StatValue4", "StatValue5", "StatValue6", "StatValue7", "StatValue8", "StatValue9", "SubClass" },
                values: new object[,]
                {
                    { 59m, "Warrior,Wizard,Hunter,Healer", 0L, 3, null, null, null, null, null, null, 59L, 2304, null, 20L, "Blightfly Wing", 1, null, 0L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 300 },
                    { 60m, "Warrior,Wizard,Hunter,Healer", 0L, 3, null, null, null, null, null, null, 60L, 2304, null, 20L, "Fen Wolf Pelt", 1, null, 0L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 300 },
                    { 61m, "Warrior,Wizard,Hunter,Healer", 0L, 3, null, null, null, null, null, null, 61L, 2304, null, 20L, "Ledger Page", 1, null, 0L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 300 },
                    { 62m, "Warrior,Wizard,Hunter,Healer", 0L, 3, null, null, null, null, null, null, 62L, 2304, null, 20L, "Tuskroot Heartwood", 1, null, 0L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 300 },
                    { 63m, "Warrior,Wizard,Hunter,Healer", 0L, 3, null, null, null, null, null, null, 63L, 2304, null, 20L, "Bramble Heart", 1, null, 0L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 300 },
                    { 64m, "Warrior,Wizard,Hunter,Healer", 400L, 2, null, null, null, null, null, null, 64L, 0, 5, 1L, "Heartwood Band", 2, 5, 100L, 7, 0, null, 4, null, null, null, null, null, null, null, 3L, null, 2L, null, null, null, null, null, null, null, 207 },
                    { 65m, "Warrior,Wizard,Hunter,Healer", 800L, 2, null, null, null, null, null, null, 65L, 0, 7, 1L, "Thornheart Signet", 3, 7, 200L, 7, 0, null, 4, null, null, null, null, null, null, null, 5L, null, 4L, null, null, null, null, null, null, null, 207 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 59m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 60m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 61m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 62m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 63m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 64m);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 65m);
        }
    }
}
