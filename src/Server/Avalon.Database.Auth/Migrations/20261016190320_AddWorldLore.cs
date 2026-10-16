using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AddWorldLore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Lore",
                table: "Worlds",
                type: "character varying(280)",
                maxLength: 280,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 1,
                column: "Lore",
                value: null);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 2,
                column: "Lore",
                value: null);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 3,
                column: "Lore",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Lore",
                table: "Worlds");
        }
    }
}
