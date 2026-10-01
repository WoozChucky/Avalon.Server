using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class RelayoutTownNpcs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Facing", "OffsetX" },
                values: new object[] { 180f, 0f });

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
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 204f, 35f, 41f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 156f, 25f, 41f });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Facing", "OffsetX" },
                values: new object[] { 143f, -3f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 217f, 3f, 4f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 180f, 0f, 7f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 135f, -6f, 6f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 114f, -9f, 4f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 132f, -9f, 8f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 149f, -6f, 10f });
        }
    }
}
