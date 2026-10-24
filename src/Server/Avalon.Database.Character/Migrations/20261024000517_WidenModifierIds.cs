using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <summary>
    /// #1179: the catalog modifier an item names (ItemInstanceModifiers.ModifierId, and the trade record's copy) widens
    /// from smallint to integer, the World catalog's id type, so the catalog never runs out of ids. Down narrows them back.
    /// </summary>
    public partial class WidenModifierIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ModifierId",
                table: "TradeRecordItemModifiers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AlterColumn<int>(
                name: "ModifierId",
                table: "ItemInstanceModifiers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<short>(
                name: "ModifierId",
                table: "TradeRecordItemModifiers",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<short>(
                name: "ModifierId",
                table: "ItemInstanceModifiers",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");
        }
    }
}
