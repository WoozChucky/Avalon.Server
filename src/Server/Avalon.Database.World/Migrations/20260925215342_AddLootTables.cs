using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddLootTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1-2. The two new tables, their foreign keys, the check constraint and their indexes.
            migrationBuilder.CreateTable(
                name: "LootTables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LootTables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LootTableEntries",
                columns: table => new
                {
                    LootTableId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    ItemTemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    ReferenceTableId = table.Column<int>(type: "integer", nullable: true),
                    Chance = table.Column<float>(type: "real", nullable: false),
                    GroupId = table.Column<int>(type: "integer", nullable: true),
                    MinCount = table.Column<int>(type: "integer", nullable: false),
                    MaxCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LootTableEntries", x => new { x.LootTableId, x.Sequence });
                    table.CheckConstraint("CK_LootTableEntries_ExactlyOneTarget", "(\"ItemTemplateId\" IS NULL) <> (\"ReferenceTableId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_LootTableEntries_ItemTemplates_ItemTemplateId",
                        column: x => x.ItemTemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LootTableEntries_LootTables_LootTableId",
                        column: x => x.LootTableId,
                        principalTable: "LootTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LootTableEntries_LootTables_ReferenceTableId",
                        column: x => x.ReferenceTableId,
                        principalTable: "LootTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LootTableEntries_ItemTemplateId",
                table: "LootTableEntries",
                column: "ItemTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_LootTableEntries_ReferenceTableId",
                table: "LootTableEntries",
                column: "ReferenceTableId");

            // 3. Rename, keeping the data, then allow nulls. EF scaffolds a drop and an add here, which
            //    would lose every existing value.
            migrationBuilder.RenameColumn(
                name: "LootId",
                table: "CreatureTemplates",
                newName: "LootTableId");

            migrationBuilder.AlterColumn<int>(
                name: "LootTableId",
                table: "CreatureTemplates",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            // 4. 0 meant "no loot". It has to become NULL before the foreign key exists, or the key
            //    refuses every existing row: there is no loot table 0.
            migrationBuilder.Sql("UPDATE \"CreatureTemplates\" SET \"LootTableId\" = NULL WHERE \"LootTableId\" = 0;");

            // The seed rows, as scaffolded. They write NULL, which step 4 already did; they sit after
            // the rename because before it the column they name does not exist.
            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "LootTableId",
                value: null);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "LootTableId",
                value: null);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "LootTableId",
                value: null);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "LootTableId",
                value: null);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "LootTableId",
                value: null);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "LootTableId",
                value: null);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "LootTableId",
                value: null);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "LootTableId",
                value: null);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "LootTableId",
                value: null);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "LootTableId",
                value: null);

            // 5. Index and foreign key last.
            migrationBuilder.CreateIndex(
                name: "IX_CreatureTemplates_LootTableId",
                table: "CreatureTemplates",
                column: "LootTableId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreatureTemplates_LootTables_LootTableId",
                table: "CreatureTemplates",
                column: "LootTableId",
                principalTable: "LootTables",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreatureTemplates_LootTables_LootTableId",
                table: "CreatureTemplates");

            migrationBuilder.DropIndex(
                name: "IX_CreatureTemplates_LootTableId",
                table: "CreatureTemplates");

            migrationBuilder.DropTable(
                name: "LootTableEntries");

            migrationBuilder.DropTable(
                name: "LootTables");

            migrationBuilder.Sql("UPDATE \"CreatureTemplates\" SET \"LootTableId\" = 0 WHERE \"LootTableId\" IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "LootTableId",
                table: "CreatureTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "LootTableId",
                table: "CreatureTemplates",
                newName: "LootId");

            // The seed rows, as scaffolded, after the rename that brings their column back.
            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "LootId",
                value: 0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "LootId",
                value: 0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "LootId",
                value: 0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "LootId",
                value: 0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "LootId",
                value: 0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "LootId",
                value: 0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "LootId",
                value: 0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "LootId",
                value: 0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "LootId",
                value: 0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "LootId",
                value: 0);
        }
    }
}
