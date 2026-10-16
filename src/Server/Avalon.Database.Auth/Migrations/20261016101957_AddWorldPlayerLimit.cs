using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AddWorldPlayerLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlayerLimit",
                table: "Worlds",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 1,
                column: "PlayerLimit",
                value: null);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 2,
                column: "PlayerLimit",
                value: null);

            migrationBuilder.UpdateData(
                table: "Worlds",
                keyColumn: "Id",
                keyValue: 3,
                column: "PlayerLimit",
                value: null);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Worlds_PlayerLimit",
                table: "Worlds",
                sql: "\"PlayerLimit\" IS NULL OR (\"PlayerLimit\" >= 1 AND \"PlayerLimit\" <= 100000)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Worlds_PlayerLimit",
                table: "Worlds");

            migrationBuilder.DropColumn(
                name: "PlayerLimit",
                table: "Worlds");
        }
    }
}
