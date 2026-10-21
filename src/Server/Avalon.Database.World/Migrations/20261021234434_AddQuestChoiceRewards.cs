using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestChoiceRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuestItemChoices",
                columns: table => new
                {
                    QuestId = table.Column<long>(type: "bigint", nullable: false),
                    ItemTemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestItemChoices", x => new { x.QuestId, x.ItemTemplateId });
                    table.CheckConstraint("CK_QuestItemChoices_CountPositive", "\"Count\" >= 1");
                    table.ForeignKey(
                        name: "FK_QuestItemChoices_ItemTemplates_ItemTemplateId",
                        column: x => x.ItemTemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuestItemChoices_QuestTemplates_QuestId",
                        column: x => x.QuestId,
                        principalTable: "QuestTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestItemChoices_ItemTemplateId",
                table: "QuestItemChoices",
                column: "ItemTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestItemChoices");
        }
    }
}
