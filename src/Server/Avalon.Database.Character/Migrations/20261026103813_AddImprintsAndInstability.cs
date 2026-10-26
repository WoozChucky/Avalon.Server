using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddImprintsAndInstability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "Instability",
                table: "TradeRecordItems",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "Tainted",
                table: "TradeRecordItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Imprinted",
                table: "TradeRecordItemModifiers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "Instability",
                table: "ItemInstances",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "Tainted",
                table: "ItemInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Imprinted",
                table: "ItemInstanceModifiers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TradeRecordItems_Instability",
                table: "TradeRecordItems",
                sql: "\"Instability\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TradeRecordItems_Tainted",
                table: "TradeRecordItems",
                sql: "\"Tainted\" = (\"Instability\" >= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemInstances_Instability",
                table: "ItemInstances",
                sql: "\"Instability\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemInstances_Tainted",
                table: "ItemInstances",
                sql: "\"Tainted\" = (\"Instability\" >= 100)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TradeRecordItems_Instability",
                table: "TradeRecordItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TradeRecordItems_Tainted",
                table: "TradeRecordItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemInstances_Instability",
                table: "ItemInstances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemInstances_Tainted",
                table: "ItemInstances");

            migrationBuilder.DropColumn(
                name: "Instability",
                table: "TradeRecordItems");

            migrationBuilder.DropColumn(
                name: "Tainted",
                table: "TradeRecordItems");

            migrationBuilder.DropColumn(
                name: "Imprinted",
                table: "TradeRecordItemModifiers");

            migrationBuilder.DropColumn(
                name: "Instability",
                table: "ItemInstances");

            migrationBuilder.DropColumn(
                name: "Tainted",
                table: "ItemInstances");

            migrationBuilder.DropColumn(
                name: "Imprinted",
                table: "ItemInstanceModifiers");
        }
    }
}
