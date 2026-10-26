using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddModifierSeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SealProgress",
                table: "TradeRecordItemModifiers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SealProgress",
                table: "ItemInstanceModifiers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TradeRecordItemModifiers_SealProgress",
                table: "TradeRecordItemModifiers",
                sql: "\"SealProgress\" IS NULL OR \"SealProgress\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstanceModifiers_Sealed",
                table: "ItemInstanceModifiers",
                column: "ItemInstanceId",
                unique: true,
                filter: "\"SealProgress\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemInstanceModifiers_SealProgress",
                table: "ItemInstanceModifiers",
                sql: "\"SealProgress\" IS NULL OR \"SealProgress\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TradeRecordItemModifiers_SealProgress",
                table: "TradeRecordItemModifiers");

            migrationBuilder.DropIndex(
                name: "IX_ItemInstanceModifiers_Sealed",
                table: "ItemInstanceModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemInstanceModifiers_SealProgress",
                table: "ItemInstanceModifiers");

            migrationBuilder.DropColumn(
                name: "SealProgress",
                table: "TradeRecordItemModifiers");

            migrationBuilder.DropColumn(
                name: "SealProgress",
                table: "ItemInstanceModifiers");
        }
    }
}
