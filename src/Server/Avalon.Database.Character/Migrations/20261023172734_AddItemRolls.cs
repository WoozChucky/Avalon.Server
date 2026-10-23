using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddItemRolls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Flags",
                table: "TradeRecordItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Rolls",
                table: "TradeRecordItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rolls",
                table: "ItemInstances",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Flags",
                table: "TradeRecordItems");

            migrationBuilder.DropColumn(
                name: "Rolls",
                table: "TradeRecordItems");

            migrationBuilder.DropColumn(
                name: "Rolls",
                table: "ItemInstances");
        }
    }
}
