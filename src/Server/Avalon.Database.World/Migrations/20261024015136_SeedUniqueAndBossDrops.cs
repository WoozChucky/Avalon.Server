using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedUniqueAndBossDrops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "LootTableEntries",
                columns: new[] { "LootTableId", "Sequence", "Chance", "GroupId", "ItemTemplateId", "MaxCount", "MinCount", "ReferenceTableId" },
                values: new object[,]
                {
                    { 6, 8, 10f, null, 58m, 1, 1, null },
                    { 7, 8, 10f, null, 64m, 1, 1, null },
                    { 7, 9, 10f, null, 67m, 1, 1, null },
                    { 8, 9, 15f, null, 65m, 1, 1, null },
                    { 8, 10, 15f, null, 66m, 1, 1, null }
                });

            migrationBuilder.InsertData(
                table: "LootTables",
                columns: new[] { "Id", "Name" },
                values: new object[] { 15, "Forest boss gear" });

            migrationBuilder.InsertData(
                table: "LootTableEntries",
                columns: new[] { "LootTableId", "Sequence", "Chance", "GroupId", "ItemTemplateId", "MaxCount", "MinCount", "ReferenceTableId" },
                values: new object[,]
                {
                    { 8, 8, 100f, null, null, 1, 1, 15 },
                    { 15, 1, 1f, 1, 32m, 1, 1, null },
                    { 15, 2, 1f, 1, 35m, 1, 1, null },
                    { 15, 3, 1f, 1, 33m, 1, 1, null },
                    { 15, 4, 1f, 1, 34m, 1, 1, null },
                    { 15, 5, 1f, 1, 70m, 1, 1, null },
                    { 15, 6, 1f, 1, 73m, 1, 1, null },
                    { 15, 7, 1f, 1, 36m, 1, 1, null },
                    { 15, 8, 1f, 1, 37m, 1, 1, null },
                    { 15, 9, 1f, 1, 38m, 1, 1, null },
                    { 15, 10, 1f, 1, 39m, 1, 1, null },
                    { 15, 11, 1f, 1, 40m, 1, 1, null },
                    { 15, 12, 1f, 1, 76m, 1, 1, null },
                    { 15, 13, 1f, 1, 46m, 1, 1, null },
                    { 15, 14, 1f, 1, 47m, 1, 1, null },
                    { 15, 15, 1f, 1, 48m, 1, 1, null },
                    { 15, 16, 1f, 1, 49m, 1, 1, null },
                    { 15, 17, 1f, 1, 50m, 1, 1, null },
                    { 15, 18, 1f, 1, 77m, 1, 1, null },
                    { 15, 19, 1f, 1, 41m, 1, 1, null },
                    { 15, 20, 1f, 1, 42m, 1, 1, null },
                    { 15, 21, 1f, 1, 43m, 1, 1, null },
                    { 15, 22, 1f, 1, 44m, 1, 1, null },
                    { 15, 23, 1f, 1, 45m, 1, 1, null },
                    { 15, 24, 1f, 1, 78m, 1, 1, null },
                    { 15, 25, 1f, 1, 68m, 1, 1, null },
                    { 15, 26, 1f, 1, 69m, 1, 1, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 6, 8 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 7, 8 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 7, 9 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 8, 8 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 8, 9 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 8, 10 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 3 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 4 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 5 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 6 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 7 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 8 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 9 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 10 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 11 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 12 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 13 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 14 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 15 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 16 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 17 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 18 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 19 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 20 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 21 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 22 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 23 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 24 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 25 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 15, 26 });

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 15);
        }
    }
}
