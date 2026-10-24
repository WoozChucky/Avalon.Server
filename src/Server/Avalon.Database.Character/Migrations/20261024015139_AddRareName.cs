using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddRareName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RareName",
                table: "TradeRecordItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RareName",
                table: "ItemInstances",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemInstances_RareName",
                table: "ItemInstances",
                sql: "(\"Rarity\" = 2) = (\"RareName\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemInstances_RareName",
                table: "ItemInstances");

            migrationBuilder.DropColumn(
                name: "RareName",
                table: "TradeRecordItems");

            migrationBuilder.DropColumn(
                name: "RareName",
                table: "ItemInstances");
        }
    }
}
