using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedBoarCullDaily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "LocalizedTextLocales",
                columns: new[] { "Locale", "TextId", "Text" },
                values: new object[,]
                {
                    { "ptPT", 75, "Abate de Javalis" },
                    { "ptPT", 76, "Os Thornback Boars voltaram a revolver todos os caminhos para Glimmerdell, {name}, e cada couro que rasgam acaba remendado por mim. Desbasta-os: quinze chegam por hoje. Volta amanhã, que haverá mais." },
                    { "ptPT", 77, "Menos quinze presas nas sebes. Toma, pelo teu trabalho. Pela manhã estão de volta, atenção. Voltam sempre." },
                    { "ptPT", 78, "Thornback Boars abatidos" }
                });

            migrationBuilder.InsertData(
                table: "LocalizedTexts",
                columns: new[] { "Id", "Text" },
                values: new object[,]
                {
                    { 75, "Boar Cull" },
                    { 76, "The Thornback Boars are rooting up every path into Glimmerdell again, {name}, and every hide they tear is one I end up patching. Thin them out: fifteen will do for today. Come back tomorrow; there will be more." },
                    { 77, "Fifteen fewer tusks in the hedges. Here, for your trouble. They will be back by morning, mind. They always are." },
                    { 78, "Thornback Boars culled" }
                });

            migrationBuilder.InsertData(
                table: "QuestTemplates",
                columns: new[] { "Id", "ClassRequirement", "CompletionTextId", "DescriptionTextId", "EnderCreatureId", "Environment", "GiverCreatureId", "IsRepeatable", "LevelRequirement", "Rarity", "RepeatFrequency", "RequiredQuestId", "RewardExperience", "RewardMoney", "ScriptName", "TitleTextId", "Type" },
                values: new object[] { 10L, null, 77, 76, 13m, 0, 13m, true, 8, 0, 1, null, 800L, 500m, null, 75, 0 });

            migrationBuilder.InsertData(
                table: "QuestStages",
                columns: new[] { "QuestId", "Sequence", "DescriptionTextId" },
                values: new object[] { 10L, 0, null });

            migrationBuilder.InsertData(
                table: "QuestObjectives",
                columns: new[] { "Id", "Count", "CreatureTemplateId", "DescriptionTextId", "DoodadTemplateId", "ItemTemplateId", "QuestId", "StageSequence", "Type" },
                values: new object[] { 1001L, 15L, 4m, 78, null, null, 10L, 0, 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 75 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 76 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 77 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 78 });

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 75);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 76);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 77);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 78);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 1001L);

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 10L, 0 });

            migrationBuilder.DeleteData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 10L);
        }
    }
}
