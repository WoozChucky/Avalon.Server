using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatureDisplayId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DisplayId",
                table: "CreatureTemplates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "DisplayId",
                value: 1L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "DisplayId",
                value: 2L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "DisplayId",
                value: 3L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "DisplayId",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "DisplayId",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "DisplayId",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "DisplayId",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "DisplayId",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "DisplayId",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "DisplayId",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                column: "DisplayId",
                value: 4L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                column: "DisplayId",
                value: 5L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                column: "DisplayId",
                value: 6L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                column: "DisplayId",
                value: 7L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayId",
                table: "CreatureTemplates");
        }
    }
}
