using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class AddMoneyAndItemInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Money",
                table: "Characters",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ItemInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    Durability = table.Column<long>(type: "bigint", nullable: false),
                    Charges = table.Column<long>(type: "bigint", nullable: false),
                    Flags = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemInstances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterInventory_ItemId",
                table: "CharacterInventory",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstances_CharacterId",
                table: "ItemInstances",
                column: "CharacterId");

            // Spec #459, "Carrying existing rows across": the foreign key below cannot be added while
            // slot rows point at items that are not in this database yet, which on the first run is
            // every row. This is the migration's only data change. The owner's one-off carry-over
            // script puts the rows back afterwards; any other environment simply starts empty.
            migrationBuilder.Sql("""
                DELETE FROM "CharacterInventory" AS ci
                WHERE NOT EXISTS (SELECT 1 FROM "ItemInstances" AS ii WHERE ii."Id" = ci."ItemId");
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_CharacterInventory_ItemInstances_ItemId",
                table: "CharacterInventory",
                column: "ItemId",
                principalTable: "ItemInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CharacterInventory_ItemInstances_ItemId",
                table: "CharacterInventory");

            migrationBuilder.DropTable(
                name: "ItemInstances");

            migrationBuilder.DropIndex(
                name: "IX_CharacterInventory_ItemId",
                table: "CharacterInventory");

            migrationBuilder.DropColumn(
                name: "Money",
                table: "Characters");
        }
    }
}
