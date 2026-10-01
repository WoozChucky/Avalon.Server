using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedQuestStoryline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ItemTemplates",
                columns: new[] { "Id", "AllowedClasses", "BuyPrice", "Class", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2", "DisplayId", "Flags", "ItemPower", "MaxStackSize", "Name", "Rarity", "RequiredLevel", "SellPrice", "Slot", "StatType1", "StatType10", "StatType2", "StatType3", "StatType4", "StatType5", "StatType6", "StatType7", "StatType8", "StatType9", "StatValue1", "StatValue10", "StatValue2", "StatValue3", "StatValue4", "StatValue5", "StatValue6", "StatValue7", "StatValue8", "StatValue9", "SubClass" },
                values: new object[,]
                {
                    { 57m, "Warrior,Wizard,Hunter,Healer", 0L, 3, null, null, null, null, null, null, 57L, 2304, null, 20L, "Boar Tusk", 1, null, 0L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 300 },
                    { 58m, "Warrior,Wizard,Hunter,Healer", 200L, 2, null, null, null, null, null, null, 58L, 0, 3, 1L, "Alpha's Fang Pendant", 2, 2, 50L, 1, 0, null, 4, null, null, null, null, null, null, null, 2L, null, 1L, null, null, null, null, null, null, null, 206 }
                });

            migrationBuilder.InsertData(
                table: "LocalizedTextLocales",
                columns: new[] { "Locale", "TextId", "Text" },
                values: new object[,]
                {
                    { "ptPT", 25, "Desbastar a Manada" },
                    { "ptPT", 26, "Os Thornback Boars ganharam ousadia, {name}. Revolvem os caminhos e atacam com as presas quem se aproxima. Abate seis deles antes que mais alguém tenha de ser trazido para casa." },
                    { "ptPT", 27, "Menos seis presas no mato. Os caminhos vão respirar melhor por isso." },
                    { "ptPT", 28, "Thornback Boars abatidos" },
                    { "ptPT", 29, "Presas para o Borin" },
                    { "ptPT", 30, "O Borin jura que a presa de javali ganha gume como mais nada. Leva-lhe quatro presas dos Thornback Boars; ele há de fazer valer a pena." },
                    { "ptPT", 31, "Quatro boas presas! Depois de temperadas, vão dar coisa fina. Leva isto para a estrada." },
                    { "ptPT", 32, "Presas de javali recolhidas" },
                    { "ptPT", 33, "O Uivo do Alfa" },
                    { "ptPT", 34, "Agora há algo a liderar a alcateia, {name}. Desbasta os lobos e as carcaças na orla da floresta, conta à Marta o que viste, e depois encontra o Bramblemaw Alpha e acaba com ele." },
                    { "ptPT", 35, "Os uivos pararam. A floresta não estava tão calma há muito tempo. Usa isto, {class}; mereceste-o." },
                    { "ptPT", 36, "Grey Fen Wolves abatidos" },
                    { "ptPT", 37, "Husks of the Wold destruídos" },
                    { "ptPT", 38, "Fala com a Marta Ledgerwell" },
                    { "ptPT", 39, "Bramblemaw Alpha abatido" },
                    { "ptPT", 40, "Limpa a orla da floresta." },
                    { "ptPT", 41, "Conta à Marta o que viste." },
                    { "ptPT", 42, "Caça o Bramblemaw Alpha." }
                });

            migrationBuilder.InsertData(
                table: "LocalizedTexts",
                columns: new[] { "Id", "Text" },
                values: new object[,]
                {
                    { 25, "Thinning the Herd" },
                    { 26, "The Thornback Boars have grown bold, {name}. They root up the paths and gore anyone who strays. Cull six of them before someone else is carried home." },
                    { 27, "Six fewer tusks in the undergrowth. The paths will breathe easier for it." },
                    { 28, "Thornback Boars slain" },
                    { 29, "Tusks for Borin" },
                    { 30, "Borin swears boar tusk takes an edge like nothing else. Bring him four tusks from the Thornback Boars; he will make it worth your while." },
                    { 31, "Four good tusks! These will temper into something fine. Take these for the road." },
                    { 32, "Boar Tusks gathered" },
                    { 33, "The Alpha's Howl" },
                    { 34, "Something leads the pack now, {name}. Thin the wolves and the husks at the forest's edge, tell Marta what you have seen, then find the Bramblemaw Alpha and end it." },
                    { 35, "The howling has stopped. The forest is quieter than it has been in a long while. Wear this, {class}; you earned it." },
                    { 36, "Grey Fen Wolves slain" },
                    { 37, "Husks of the Wold destroyed" },
                    { 38, "Speak with Marta Ledgerwell" },
                    { 39, "Bramblemaw Alpha slain" },
                    { 40, "Clear the forest's edge." },
                    { 41, "Tell Marta what you have seen." },
                    { 42, "Hunt down the Bramblemaw Alpha." }
                });

            migrationBuilder.InsertData(
                table: "QuestTemplates",
                columns: new[] { "Id", "ClassRequirement", "CompletionTextId", "DescriptionTextId", "EnderCreatureId", "Environment", "GiverCreatureId", "IsRepeatable", "LevelRequirement", "Rarity", "RepeatFrequency", "RequiredQuestId", "RewardExperience", "RewardMoney", "ScriptName", "TitleTextId", "Type" },
                values: new object[] { 1L, null, 27, 26, 1m, 0, 1m, false, 1, 0, null, null, 150L, 100m, null, 25, 0 });

            migrationBuilder.InsertData(
                table: "QuestStages",
                columns: new[] { "QuestId", "Sequence", "DescriptionTextId" },
                values: new object[] { 1L, 0, null });

            migrationBuilder.InsertData(
                table: "QuestTemplates",
                columns: new[] { "Id", "ClassRequirement", "CompletionTextId", "DescriptionTextId", "EnderCreatureId", "Environment", "GiverCreatureId", "IsRepeatable", "LevelRequirement", "Rarity", "RepeatFrequency", "RequiredQuestId", "RewardExperience", "RewardMoney", "ScriptName", "TitleTextId", "Type" },
                values: new object[] { 2L, null, 31, 30, 2m, 0, 1m, false, 1, 0, null, 1L, 250L, 150m, null, 29, 0 });

            migrationBuilder.InsertData(
                table: "QuestItemRewards",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[] { 56m, 2L, 2L });

            migrationBuilder.InsertData(
                table: "QuestObjectives",
                columns: new[] { "Id", "Count", "CreatureTemplateId", "DescriptionTextId", "ItemTemplateId", "QuestId", "StageSequence", "Type" },
                values: new object[] { 101L, 6L, 4m, 28, null, 1L, 0, 1 });

            migrationBuilder.InsertData(
                table: "QuestStages",
                columns: new[] { "QuestId", "Sequence", "DescriptionTextId" },
                values: new object[] { 2L, 0, null });

            migrationBuilder.InsertData(
                table: "QuestTemplates",
                columns: new[] { "Id", "ClassRequirement", "CompletionTextId", "DescriptionTextId", "EnderCreatureId", "Environment", "GiverCreatureId", "IsRepeatable", "LevelRequirement", "Rarity", "RepeatFrequency", "RequiredQuestId", "RewardExperience", "RewardMoney", "ScriptName", "TitleTextId", "Type" },
                values: new object[] { 3L, null, 35, 34, 2m, 0, 2m, false, 2, 0, null, 2L, 600L, 400m, null, 33, 0 });

            migrationBuilder.InsertData(
                table: "QuestItemRewards",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[] { 58m, 3L, 1L });

            migrationBuilder.InsertData(
                table: "QuestObjectives",
                columns: new[] { "Id", "Count", "CreatureTemplateId", "DescriptionTextId", "ItemTemplateId", "QuestId", "StageSequence", "Type" },
                values: new object[] { 201L, 4L, null, 32, 57m, 2L, 0, 2 });

            migrationBuilder.InsertData(
                table: "QuestStages",
                columns: new[] { "QuestId", "Sequence", "DescriptionTextId" },
                values: new object[,]
                {
                    { 3L, 0, 40 },
                    { 3L, 1, 41 },
                    { 3L, 2, 42 }
                });

            migrationBuilder.InsertData(
                table: "QuestItemDrops",
                columns: new[] { "CreatureTemplateId", "ObjectiveId", "Chance" },
                values: new object[] { 4m, 201L, 60f });

            migrationBuilder.InsertData(
                table: "QuestObjectives",
                columns: new[] { "Id", "Count", "CreatureTemplateId", "DescriptionTextId", "ItemTemplateId", "QuestId", "StageSequence", "Type" },
                values: new object[,]
                {
                    { 301L, 3L, 5m, 36, null, 3L, 0, 1 },
                    { 302L, 2L, 7m, 37, null, 3L, 0, 1 },
                    { 303L, 1L, 11m, 38, null, 3L, 1, 3 },
                    { 304L, 1L, 8m, 39, null, 3L, 2, 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 25 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 26 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 27 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 28 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 29 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 30 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 31 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 32 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 33 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 34 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 35 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 36 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 37 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 38 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 39 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 40 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 41 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 42 });

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 25);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 27);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 28);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 29);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 30);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 31);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 32);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 33);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 34);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 35);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 36);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 37);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 38);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 39);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 40);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 41);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 42);

            migrationBuilder.DeleteData(
                table: "QuestItemDrops",
                keyColumns: new[] { "CreatureTemplateId", "ObjectiveId" },
                keyValues: new object[] { 4m, 201L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 56m, 2L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 58m, 3L });

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 101L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 301L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 302L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 303L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 304L);

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 58m);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 201L);

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 1L, 0 });

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 3L, 0 });

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 3L, 1 });

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 3L, 2 });

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 57m);

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 2L, 0 });

            migrationBuilder.DeleteData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 1L);
        }
    }
}
