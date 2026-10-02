using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class RaiseForestMaxPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MapTemplates",
                keyColumn: "Id",
                keyValue: 2,
                column: "MaxPlayers",
                value: 6);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "MapTemplates",
                keyColumn: "Id",
                keyValue: 2,
                column: "MaxPlayers",
                value: 1);
        }
    }
}
