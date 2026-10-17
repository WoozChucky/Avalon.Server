using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class GlimmerdellKitLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 194f, 1.9f, 7.85f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 262f, 1.4f, 22.2f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 297f, 35.5f, 27.2f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 87f, 23f, 29.6f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 260f, 37.5f, 1.3f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 6,
                column: "OffsetX",
                value: 37.4f);

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 127f, 22f, 6f });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 191f, 1.6f, 8f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 270f, 1.9f, 22f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 246f, 35f, 32.2f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 118f, 21.8f, 34.4f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 118f, 24f, 3.2f });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 6,
                column: "OffsetX",
                value: 38.9f);

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 222f, 36.5f, 7.2f });
        }
    }
}
