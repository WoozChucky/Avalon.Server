using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class MoveTownNpcsToTheirBuildings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 90f, -4f, 0f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 270f, 4.4f, 22f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Facing", "OffsetZ" },
                values: new object[] { 226f, 34.8f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Facing", "OffsetZ" },
                values: new object[] { 148f, 38f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 49f, 24.2f, -5f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 270f, 41.6f, 0f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 211f, 36.5f, 10.7f });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 180f, 0f, 4f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 198f, 4f, 12f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Facing", "OffsetZ" },
                values: new object[] { 204f, 41f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Facing", "OffsetZ" },
                values: new object[] { 156f, 41f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 24f, 25f, -11f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 336f, 35f, -11f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 270f, 41f, 0f });
        }
    }
}
