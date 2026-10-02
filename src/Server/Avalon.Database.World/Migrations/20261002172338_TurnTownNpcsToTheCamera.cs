using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class TurnTownNpcsToTheCamera : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
                column: "OffsetX",
                value: 1.9f);

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Facing", "OffsetZ" },
                values: new object[] { 246f, 32.2f });

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
                columns: new[] { "Facing", "OffsetZ" },
                values: new object[] { 222f, 7.2f });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
                column: "OffsetX",
                value: 4.4f);

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
                columns: new[] { "Facing", "OffsetX", "OffsetZ" },
                values: new object[] { 148f, 25f, 38f });

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
                column: "OffsetX",
                value: 41.6f);

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Facing", "OffsetZ" },
                values: new object[] { 211f, 10.7f });
        }
    }
}
