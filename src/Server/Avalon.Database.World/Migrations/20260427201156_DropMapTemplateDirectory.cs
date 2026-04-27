using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class DropMapTemplateDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Directory",
                table: "MapTemplates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Directory",
                table: "MapTemplates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "MapTemplates",
                keyColumn: "Id",
                keyValue: 1,
                column: "Directory",
                value: "Maps/");

            migrationBuilder.UpdateData(
                table: "MapTemplates",
                keyColumn: "Id",
                keyValue: 2,
                column: "Directory",
                value: "Maps/");
        }
    }
}
