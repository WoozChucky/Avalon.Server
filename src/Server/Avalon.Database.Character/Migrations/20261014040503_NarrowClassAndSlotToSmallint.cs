using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class NarrowClassAndSlotToSmallint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "Class",
                table: "Characters",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<byte>(
                name: "Slot",
                table: "CharacterInventory",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Class",
                table: "Characters",
                type: "integer",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<int>(
                name: "Slot",
                table: "CharacterInventory",
                type: "integer",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");
        }
    }
}
