using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <summary>
    /// #711: the QuestItemChoices table, quest 3's choice of weapon and quest 11, "Earn It Fancy". Up's inserts are
    /// ordered by hand, parents first: the texts, the quest, its stage and objectives, then the choice rows.
    /// </summary>
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

            migrationBuilder.InsertData(
                table: "LocalizedTexts",
                columns: new[] { "Id", "Text" },
                values: new object[,]
                {
                    { 79, "Earn It Fancy" },
                    { 80, "The pelts you brought me lined half the town's cloaks, {name}, and not one of them was cut for you. That wants mending. But you know my rule: buy it plain, earn it fancy. Put down six Grey Fen Wolves and the Bramblemaw Alpha that leads them, and I will fit you myself." },
                    { 81, "The pack thinned and its alpha with it. You earned it fancy, {class}. Take your pick; I made every piece on that rack to be fought in." }
                });

            migrationBuilder.InsertData(
                table: "LocalizedTextLocales",
                columns: new[] { "Locale", "TextId", "Text" },
                values: new object[,]
                {
                    { "ptPT", 79, "Ganha-o Vistoso" },
                    { "ptPT", 80, "As peles que me trouxeste forraram metade das capas da vila, {name}, e nenhuma foi talhada para ti. Isso tem de se remediar. Mas conheces a minha regra: compra-o simples, ganha-o vistoso. Abate seis Grey Fen Wolves e o Bramblemaw Alpha que os lidera, e eu própria tiro-te as medidas." },
                    { "ptPT", 81, "A alcateia desbastada, e o alfa com ela. Ganhaste-o vistoso, {class}. Escolhe a tua peça; fiz cada uma daquelas para aguentar uma luta." }
                });

            migrationBuilder.InsertData(
                table: "QuestTemplates",
                columns: new[] { "Id", "ClassRequirement", "CompletionTextId", "DescriptionTextId", "EnderCreatureId", "Environment", "GiverCreatureId", "IsRepeatable", "LevelRequirement", "Rarity", "RepeatFrequency", "RequiredQuestId", "RewardExperience", "RewardMoney", "ScriptName", "TitleTextId", "Type" },
                values: new object[] { 11L, null, 81, 80, 13m, 0, 13m, false, 4, 0, null, 5L, 550L, 325m, null, 79, 0 });

            migrationBuilder.InsertData(
                table: "QuestStages",
                columns: new[] { "QuestId", "Sequence", "DescriptionTextId" },
                values: new object[] { 11L, 0, null });

            migrationBuilder.InsertData(
                table: "QuestObjectives",
                columns: new[] { "Id", "Count", "CreatureTemplateId", "DescriptionTextId", "DoodadTemplateId", "ItemTemplateId", "QuestId", "StageSequence", "Type" },
                values: new object[,]
                {
                    { 1101L, 6L, 5m, 36, null, null, 11L, 0, 1 },
                    { 1102L, 1L, 8m, 39, null, null, 11L, 0, 1 }
                });

            migrationBuilder.InsertData(
                table: "QuestItemChoices",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[,]
                {
                    { 5m, 3L, 1L },
                    { 6m, 3L, 1L },
                    { 7m, 3L, 1L },
                    { 8m, 3L, 1L },
                    { 13m, 11L, 1L },
                    { 18m, 11L, 1L },
                    { 23m, 11L, 1L },
                    { 28m, 11L, 1L }
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

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 79 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 80 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 81 });

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 79);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 80);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 81);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 1101L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 1102L);

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 11L, 0 });

            migrationBuilder.DeleteData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 11L);
        }
    }
}
