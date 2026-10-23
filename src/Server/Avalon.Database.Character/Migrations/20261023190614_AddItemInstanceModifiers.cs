using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <summary>
    /// #1179: every item instance gets its own rarity and item level, and its modifiers as rows (ItemInstanceModifiers,
    /// their rolled numbers in ItemInstanceModifierValues, both cascading from the item); a trade record keeps its own
    /// copy of what moved (TradeRecordItems' two columns, TradeRecordItemModifiers, TradeRecordItemModifierValues). The
    /// rows already stored (consumables and quest items, whose level nothing reads) take Rarity 0 and ItemLevel 1: the
    /// ItemLevel default is 1, not the generated 0, so the new check holds for them.
    /// </summary>
    public partial class AddItemInstanceModifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "ItemLevel",
                table: "TradeRecordItems",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<byte>(
                name: "Rarity",
                table: "TradeRecordItems",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<short>(
                name: "ItemLevel",
                table: "ItemInstances",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<byte>(
                name: "Rarity",
                table: "ItemInstances",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "ItemInstanceModifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifierId = table.Column<short>(type: "smallint", nullable: false),
                    Ordinal = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemInstanceModifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemInstanceModifiers_ItemInstances_ItemInstanceId",
                        column: x => x.ItemInstanceId,
                        principalTable: "ItemInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeRecordItemModifiers",
                columns: table => new
                {
                    TradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<byte>(type: "smallint", nullable: false),
                    ModifierId = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRecordItemModifiers", x => new { x.TradeId, x.ItemInstanceId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_TradeRecordItemModifiers_TradeRecordItems_TradeId_ItemInsta~",
                        columns: x => new { x.TradeId, x.ItemInstanceId },
                        principalTable: "TradeRecordItems",
                        principalColumns: new[] { "TradeId", "ItemInstanceId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemInstanceModifierValues",
                columns: table => new
                {
                    ItemInstanceModifierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<byte>(type: "smallint", nullable: false),
                    Stat = table.Column<short>(type: "smallint", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    Value2 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemInstanceModifierValues", x => new { x.ItemInstanceModifierId, x.Index });
                    table.CheckConstraint("CK_ItemInstanceModifierValues_Range", "\"Value2\" IS NULL OR \"Value2\" >= \"Value\"");
                    table.ForeignKey(
                        name: "FK_ItemInstanceModifierValues_ItemInstanceModifiers_ItemInstan~",
                        column: x => x.ItemInstanceModifierId,
                        principalTable: "ItemInstanceModifiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeRecordItemModifierValues",
                columns: table => new
                {
                    TradeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<byte>(type: "smallint", nullable: false),
                    Index = table.Column<byte>(type: "smallint", nullable: false),
                    Stat = table.Column<short>(type: "smallint", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    Value2 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRecordItemModifierValues", x => new { x.TradeId, x.ItemInstanceId, x.Ordinal, x.Index });
                    table.ForeignKey(
                        name: "FK_TradeRecordItemModifierValues_TradeRecordItemModifiers_Trad~",
                        columns: x => new { x.TradeId, x.ItemInstanceId, x.Ordinal },
                        principalTable: "TradeRecordItemModifiers",
                        principalColumns: new[] { "TradeId", "ItemInstanceId", "Ordinal" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemInstances_ItemLevel",
                table: "ItemInstances",
                sql: "\"ItemLevel\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemInstances_Rarity",
                table: "ItemInstances",
                sql: "\"Rarity\" BETWEEN 0 AND 3");

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstanceModifiers_ItemInstanceId",
                table: "ItemInstanceModifiers",
                column: "ItemInstanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemInstanceModifierValues");

            migrationBuilder.DropTable(
                name: "TradeRecordItemModifierValues");

            migrationBuilder.DropTable(
                name: "ItemInstanceModifiers");

            migrationBuilder.DropTable(
                name: "TradeRecordItemModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemInstances_ItemLevel",
                table: "ItemInstances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemInstances_Rarity",
                table: "ItemInstances");

            migrationBuilder.DropColumn(
                name: "ItemLevel",
                table: "TradeRecordItems");

            migrationBuilder.DropColumn(
                name: "Rarity",
                table: "TradeRecordItems");

            migrationBuilder.DropColumn(
                name: "ItemLevel",
                table: "ItemInstances");

            migrationBuilder.DropColumn(
                name: "Rarity",
                table: "ItemInstances");
        }
    }
}
