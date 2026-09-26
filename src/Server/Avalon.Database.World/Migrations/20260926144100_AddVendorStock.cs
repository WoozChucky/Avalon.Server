using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VendorStocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    CreatureTemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    ItemTemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    MaxStock = table.Column<long>(type: "bigint", nullable: true),
                    RestockSeconds = table.Column<long>(type: "bigint", nullable: true),
                    PriceOverride = table.Column<long>(type: "bigint", nullable: true),
                    RequiredQuestId = table.Column<long>(type: "bigint", nullable: true),
                    RequiredQuestState = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorStocks", x => x.Id);
                    table.CheckConstraint("CK_VendorStocks_MaxStockPositive", "\"MaxStock\" IS NULL OR \"MaxStock\" >= 1");
                    table.CheckConstraint("CK_VendorStocks_QuestPairs", "(\"RequiredQuestId\" IS NULL) = (\"RequiredQuestState\" IS NULL)");
                    table.CheckConstraint("CK_VendorStocks_RestockPairsWithMaxStock", "(\"MaxStock\" IS NULL) = (\"RestockSeconds\" IS NULL)");
                    table.CheckConstraint("CK_VendorStocks_RestockPositive", "\"RestockSeconds\" IS NULL OR \"RestockSeconds\" >= 1");
                    table.ForeignKey(
                        name: "FK_VendorStocks_CreatureTemplates_CreatureTemplateId",
                        column: x => x.CreatureTemplateId,
                        principalTable: "CreatureTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VendorStocks_ItemTemplates_ItemTemplateId",
                        column: x => x.ItemTemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorStockCosts",
                columns: table => new
                {
                    VendorStockId = table.Column<int>(type: "integer", nullable: false),
                    ItemTemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorStockCosts", x => new { x.VendorStockId, x.ItemTemplateId });
                    table.CheckConstraint("CK_VendorStockCosts_CountPositive", "\"Count\" >= 1");
                    table.ForeignKey(
                        name: "FK_VendorStockCosts_ItemTemplates_ItemTemplateId",
                        column: x => x.ItemTemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorStockCosts_VendorStocks_VendorStockId",
                        column: x => x.VendorStockId,
                        principalTable: "VendorStocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendorStockCosts_ItemTemplateId",
                table: "VendorStockCosts",
                column: "ItemTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorStocks_CreatureTemplateId_Sequence",
                table: "VendorStocks",
                columns: new[] { "CreatureTemplateId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorStocks_ItemTemplateId",
                table: "VendorStocks",
                column: "ItemTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VendorStockCosts");

            migrationBuilder.DropTable(
                name: "VendorStocks");
        }
    }
}
