using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class MoveLecternTurnNoticeBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MapDoodadSpawns",
                keyColumn: "Id",
                keyValue: 1,
                column: "Yaw",
                value: 180f);

            migrationBuilder.UpdateData(
                table: "MapDoodadSpawns",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "OffsetX", "OffsetZ", "Yaw" },
                values: new object[] { 26.5f, -10f, 225f });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MapDoodadSpawns",
                keyColumn: "Id",
                keyValue: 1,
                column: "Yaw",
                value: 90f);

            migrationBuilder.UpdateData(
                table: "MapDoodadSpawns",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "OffsetX", "OffsetZ", "Yaw" },
                values: new object[] { 18f, -11f, 90f });
        }
    }
}
