using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatureTemplateTimers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "Exp",
                table: "CreatureTemplates",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddColumn<int>(
                name: "BodyRemoveTimerSecs",
                table: "CreatureTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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
                columns: new[] { "BodyRemoveTimerSecs", "Exp", "RespawnTimerSecs" },
                values: new object[] { 120, 20L, 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                columns: new[] { "BodyRemoveTimerSecs", "Exp", "RespawnTimerSecs" },
                values: new object[] { 120, 20L, 180 });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                columns: new[] { "BodyRemoveTimerSecs", "Exp", "RespawnTimerSecs" },
                values: new object[] { 120, 20L, 180 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BodyRemoveTimerSecs",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "RespawnTimerSecs",
                table: "CreatureTemplates");

            migrationBuilder.AlterColumn<short>(
                name: "Exp",
                table: "CreatureTemplates",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "Exp",
                value: (short)0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "Exp",
                value: (short)0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "Exp",
                value: (short)0);
        }
    }
}
