using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.World.Database.Migrations
{
    /// <inheritdoc />
    public partial class V2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpellScript",
                table: "SpellTemplates",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "SpellTemplates",
                keyColumn: "Id",
                keyValue: 1u,
                column: "SpellScript",
                value: null);

            migrationBuilder.UpdateData(
                table: "SpellTemplates",
                keyColumn: "Id",
                keyValue: 2u,
                column: "SpellScript",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpellScript",
                table: "SpellTemplates");
        }
    }
}
