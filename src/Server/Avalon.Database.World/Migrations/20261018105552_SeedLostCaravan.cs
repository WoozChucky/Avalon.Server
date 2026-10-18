using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedLostCaravan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "DoodadTemplates",
                columns: new[] { "Id", "AreaRadius", "Collision", "CollisionHalfX", "CollisionHalfZ", "CollisionHeight", "Interaction", "InteractionRange", "InteractionTimeMs", "LootTableId", "MaxGold", "MinGold", "ModelKey", "ModelKit", "Name", "ReadyClip", "RespawnSeconds", "RestorePct", "ScriptName", "SpentLook", "UsableCondition", "UsableObjectiveId", "UsableQuestId", "UseClip", "UseLimit", "UsedClip", "UsedModelKey", "VisibleCondition", "VisibleObjectiveId", "VisibleQuestId" },
                values: new object[,]
                {
                    { 5L, 0f, (byte)0, 0f, 0f, 0f, (byte)1, 5f, 1500L, null, 0, 0, "Crate_Metal", "props-kit", "Caravan Strongbox", null, null, 0L, null, (byte)1, null, null, null, null, (byte)2, null, "avalon_strongbox_open", (byte)2, 901L, 9L },
                    { 6L, 0f, (byte)0, 0f, 0f, 0f, (byte)1, 5f, 0L, null, 0, 0, "avalon_notice_board", "town-kit", "Notice Board", null, null, 0L, null, (byte)1, (byte)2, 902L, 9L, null, (byte)2, null, "avalon_notice_board_pinned", null, null, null }
                });

            migrationBuilder.InsertData(
                table: "LocalizedTexts",
                columns: new[] { "Id", "Text" },
                values: new object[,]
                {
                    { 67, "The Lost Caravan" },
                    { 68, "Those pages were the last of the caravan's accounts, {name}. Somewhere in the wold its strongbox still lies where the wagons fell. Find it and open it, and tell me what it holds." },
                    { 69, "So that is how it ended. Thank you, {name}. Now the town knows, and I can stop waiting for wagons that will never come." },
                    { 70, "Caravan strongbox opened" },
                    { 71, "The caravan's fate posted" },
                    { 72, "Find the caravan's strongbox in the forest." },
                    { 73, "Pin the caravan's fate to the notice board in Glimmerdell." },
                    { 74, "Tell Marta what you found." }
                });

            migrationBuilder.InsertData(
                table: "LocalizedTextLocales",
                columns: new[] { "Locale", "TextId", "Text" },
                values: new object[,]
                {
                    { "ptPT", 67, "A Caravana Perdida" },
                    { "ptPT", 68, "Aquelas páginas eram as últimas contas da caravana, {name}. Algures na mata, o cofre dela ainda jaz onde as carroças tombaram. Encontra-o, abre-o e conta-me o que guarda." },
                    { "ptPT", 69, "Então foi assim que acabou. Obrigada, {name}. Agora a vila sabe, e eu posso deixar de esperar por carroças que nunca hão de chegar." },
                    { "ptPT", 70, "Cofre da caravana aberto" },
                    { "ptPT", 71, "Destino da caravana afixado" },
                    { "ptPT", 72, "Encontra o cofre da caravana na floresta." },
                    { "ptPT", 73, "Afixa o destino da caravana no quadro de avisos de Glimmerdell." },
                    { "ptPT", 74, "Conta à Marta o que encontraste." }
                });

            migrationBuilder.InsertData(
                table: "QuestTemplates",
                columns: new[] { "Id", "ClassRequirement", "CompletionTextId", "DescriptionTextId", "EnderCreatureId", "Environment", "GiverCreatureId", "IsRepeatable", "LevelRequirement", "Rarity", "RepeatFrequency", "RequiredQuestId", "RewardExperience", "RewardMoney", "ScriptName", "TitleTextId", "Type" },
                values: new object[] { 9L, null, 69, 68, 11m, 0, 11m, false, 4, 0, null, 6L, 600L, 350m, null, 67, 0 });

            migrationBuilder.InsertData(
                table: "QuestItemRewards",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[] { 56m, 9L, 2L });

            migrationBuilder.InsertData(
                table: "QuestStages",
                columns: new[] { "QuestId", "Sequence", "DescriptionTextId" },
                values: new object[,]
                {
                    { 9L, 0, 72 },
                    { 9L, 1, 73 },
                    { 9L, 2, 74 }
                });

            migrationBuilder.InsertData(
                table: "QuestObjectives",
                columns: new[] { "Id", "Count", "CreatureTemplateId", "DescriptionTextId", "DoodadTemplateId", "ItemTemplateId", "QuestId", "StageSequence", "Type" },
                values: new object[,]
                {
                    { 901L, 1L, null, 70, 5L, null, 9L, 0, 5 },
                    { 902L, 1L, null, 71, 6L, null, 9L, 1, 5 },
                    { 903L, 1L, 11m, 38, null, null, 9L, 2, 3 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 67 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 68 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 69 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 70 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 71 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 72 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 73 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 74 });

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 67);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 68);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 69);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 70);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 71);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 72);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 73);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 74);

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 56m, 9L });

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 901L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 902L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 903L);

            migrationBuilder.DeleteData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 5L);

            migrationBuilder.DeleteData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 6L);

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 9L, 0 });

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 9L, 1 });

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 9L, 2 });

            migrationBuilder.DeleteData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 9L);
        }
    }
}
