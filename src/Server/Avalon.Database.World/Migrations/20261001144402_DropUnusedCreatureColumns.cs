using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class DropUnusedCreatureColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AIName",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "RespawnTimerSecs",
                table: "CreatureTemplates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AIName",
                table: "CreatureTemplates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RespawnTimerSecs",
                table: "CreatureTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                columns: new[] { "AIName", "RespawnTimerSecs" },
                values: new object[] { "", 180 });
        }
    }
}
