using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddItemIdentified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #1179: hand-edited from the generated false. Every item before this part, and every item a trade moved, reads
            // identified. The default only fills those rows: it is dropped below, as the model declares none, so no insert
            // can leave the column out.
            migrationBuilder.AddColumn<bool>(
                name: "Identified",
                table: "TradeRecordItems",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Identified",
                table: "ItemInstances",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "Identified",
                table: "TradeRecordItems",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "Identified",
                table: "ItemInstances",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TradeRecordItems_Identified",
                table: "TradeRecordItems",
                sql: "\"Rarity\" <> 0 OR \"Identified\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemInstances_Identified",
                table: "ItemInstances",
                sql: "\"Rarity\" <> 0 OR \"Identified\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TradeRecordItems_Identified",
                table: "TradeRecordItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemInstances_Identified",
                table: "ItemInstances");

            migrationBuilder.DropColumn(
                name: "Identified",
                table: "TradeRecordItems");

            migrationBuilder.DropColumn(
                name: "Identified",
                table: "ItemInstances");
        }
    }
}
