using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedForestChainQuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "LocalizedTextLocales",
                columns: new[] { "Locale", "TextId", "Text" },
                values: new object[,]
                {
                    { "ptPT", 43, "Asas para o Alambique" },
                    { "ptPT", 44, "As Blightfly Swarmlings zumbem sobre a mata dia e noite, {name}, e o pó das asas delas acalma uma febre melhor do que qualquer raiz que eu venda. Traz-me seis asas para o meu alambique, inteiras se conseguires." },
                    { "ptPT", 45, "Seis boas asas. Esse pó vai ajudar a vencer umas quantas febres nos meses frios. Leva isto pelo teu trabalho." },
                    { "ptPT", 46, "Asas de Blightfly recolhidas" },
                    { "ptPT", 47, "Peles Antes da Geada" },
                    { "ptPT", 48, "A geada chega cedo debaixo daquelas árvores, e os Grey Fen Wolves têm os casacos mais grossos da mata. Traz-me oito das peles deles e forro metade das capas da vila antes da primeira geada." },
                    { "ptPT", 49, "Oito peles, e quase sem um rasgão. A vila vai ficar mais quente graças a isto, {name}." },
                    { "ptPT", 50, "Peles de Fen Wolf recolhidas" },
                    { "ptPT", 51, "O que as Carcaças Carregam" },
                    { "ptPT", 52, "Os viajantes juram que os Husks of the Wold ainda agarram pedaços de papel, {name}: páginas de um livro de contas, com a marca da Marta. Abate oito dessas coisas e leva as páginas que encontrares à Marta, no banco." },
                    { "ptPT", 53, "Estas são minhas. Contas de uma caravana que nunca voltou. Devo-te mais do que moedas por isto, {name}, mas moedas é o que tenho." },
                    { "ptPT", 54, "Páginas do livro de contas recuperadas" },
                    { "ptPT", 55, "Cerne" },
                    { "ptPT", 56, "O Old Tuskroot anda pela mata há mais tempo do que esta vila existe, e a madeira do seu coração é mais dura do que o ferro. Traz-me esse cerne, {class}, e engasto-o em algo que valha a pena usar." },
                    { "ptPT", 57, "Isso sim, é cerne. Estende a mão; este foi feito à tua medida." },
                    { "ptPT", 58, "Cerne de Tuskroot obtido" },
                    { "ptPT", 59, "Mãe dos Espinhos" },
                    { "ptPT", 60, "Os Bramblemaw Alphas obedecem a algo mais fundo na mata, {name}. Fala primeiro com o Garrick: ele já forjou contra espinhos. Depois vai ao coração da floresta, abate dois dos Bramblemaw Alphas e acaba com a Mother Bramble, a Mãe dos Espinhos." },
                    { "ptPT", 61, "A mata volta a respirar. Vai lembrar-se do que fizeste, e nós também. Usa isto, {class}." },
                    { "ptPT", 62, "Fala com o Garrick Emberforge" },
                    { "ptPT", 63, "Bramblemaw Alphas abatidos" },
                    { "ptPT", 64, "Coração de Bramble obtido" },
                    { "ptPT", 65, "Pergunta ao Garrick como enfrentar os espinhos." },
                    { "ptPT", 66, "Quebra o domínio dos Bramblemaw Alphas e arranca o Coração de Bramble." }
                });

            migrationBuilder.InsertData(
                table: "LocalizedTexts",
                columns: new[] { "Id", "Text" },
                values: new object[,]
                {
                    { 43, "Wings for the Still" },
                    { 44, "The Blightfly Swarmlings drone over the wold day and night, {name}, and the dust on their wings settles a fever better than any root I sell. Bring me six wings for my still, whole if you can manage it." },
                    { 45, "Six good wings. That dust will see a few fevers through the cold months. Take these for your trouble." },
                    { 46, "Blightfly Wings gathered" },
                    { 47, "Pelts Before Frost" },
                    { 48, "Frost comes early under those trees, and the Grey Fen Wolves wear the thickest coats in the wold. Bring me eight of their pelts and I will line half the town's cloaks before the first freeze." },
                    { 49, "Eight pelts, and barely a nick in them. The town will be warmer for it, {name}." },
                    { 50, "Fen Wolf Pelts gathered" },
                    { 51, "What the Husks Carry" },
                    { 52, "Travellers swear the Husks of the Wold still clutch scraps of paper, {name}: pages from a ledger, with Marta's mark on them. Put down eight of those things and bring whatever pages you find to Marta at the bank." },
                    { 53, "These are mine. Accounts from a caravan that never came back. I owe you more than coin for this, {name}, but coin is what I have." },
                    { 54, "Ledger Pages recovered" },
                    { 55, "Heartwood" },
                    { 56, "Old Tuskroot has walked the wold longer than this town has stood, and the wood at its heart is harder than iron. Bring me that heartwood, {class}, and I will set it in something worth wearing." },
                    { 57, "Now that is heartwood. Hold out your hand; this one was made to fit it." },
                    { 58, "Tuskroot Heartwood taken" },
                    { 59, "Mother of Thorns" },
                    { 60, "The Bramblemaw Alphas answer to something deeper in the wold, {name}. Speak with Garrick first: he has forged against thorns before. Then go to the heart of the forest, bring down two of the Alphas and end Mother Bramble, the Mother of Thorns." },
                    { 61, "The wold is breathing again. It will remember what you did, and so will we. Wear this, {class}." },
                    { 62, "Speak with Garrick Emberforge" },
                    { 63, "Bramblemaw Alphas slain" },
                    { 64, "Bramble Heart taken" },
                    { 65, "Ask Garrick how to face the thorns." },
                    { 66, "Break the Bramblemaw Alphas' hold and cut out the Bramble Heart." }
                });

            migrationBuilder.InsertData(
                table: "QuestTemplates",
                columns: new[] { "Id", "ClassRequirement", "CompletionTextId", "DescriptionTextId", "EnderCreatureId", "Environment", "GiverCreatureId", "IsRepeatable", "LevelRequirement", "Rarity", "RepeatFrequency", "RequiredQuestId", "RewardExperience", "RewardMoney", "ScriptName", "TitleTextId", "Type" },
                values: new object[] { 4L, null, 45, 44, 14m, 0, 14m, false, 2, 0, null, null, 300L, 200m, null, 43, 0 });

            migrationBuilder.InsertData(
                table: "QuestItemRewards",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[] { 2m, 4L, 3L });

            migrationBuilder.InsertData(
                table: "QuestStages",
                columns: new[] { "QuestId", "Sequence", "DescriptionTextId" },
                values: new object[] { 4L, 0, null });

            migrationBuilder.InsertData(
                table: "QuestTemplates",
                columns: new[] { "Id", "ClassRequirement", "CompletionTextId", "DescriptionTextId", "EnderCreatureId", "Environment", "GiverCreatureId", "IsRepeatable", "LevelRequirement", "Rarity", "RepeatFrequency", "RequiredQuestId", "RewardExperience", "RewardMoney", "ScriptName", "TitleTextId", "Type" },
                values: new object[] { 5L, null, 49, 48, 13m, 0, 13m, false, 3, 0, null, 4L, 400L, 250m, null, 47, 0 });

            migrationBuilder.InsertData(
                table: "QuestObjectives",
                columns: new[] { "Id", "Count", "CreatureTemplateId", "DescriptionTextId", "ItemTemplateId", "QuestId", "StageSequence", "Type" },
                values: new object[] { 401L, 6L, null, 46, 59m, 4L, 0, 2 });

            migrationBuilder.InsertData(
                table: "QuestStages",
                columns: new[] { "QuestId", "Sequence", "DescriptionTextId" },
                values: new object[] { 5L, 0, null });

            migrationBuilder.InsertData(
                table: "QuestTemplates",
                columns: new[] { "Id", "ClassRequirement", "CompletionTextId", "DescriptionTextId", "EnderCreatureId", "Environment", "GiverCreatureId", "IsRepeatable", "LevelRequirement", "Rarity", "RepeatFrequency", "RequiredQuestId", "RewardExperience", "RewardMoney", "ScriptName", "TitleTextId", "Type" },
                values: new object[] { 6L, null, 53, 52, 11m, 0, 3m, false, 4, 0, null, 5L, 500L, 300m, null, 51, 0 });

            migrationBuilder.InsertData(
                table: "QuestItemDrops",
                columns: new[] { "CreatureTemplateId", "ObjectiveId", "Chance" },
                values: new object[] { 6m, 401L, 50f });

            migrationBuilder.InsertData(
                table: "QuestItemRewards",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[] { 56m, 6L, 2L });

            migrationBuilder.InsertData(
                table: "QuestObjectives",
                columns: new[] { "Id", "Count", "CreatureTemplateId", "DescriptionTextId", "ItemTemplateId", "QuestId", "StageSequence", "Type" },
                values: new object[] { 501L, 8L, null, 50, 60m, 5L, 0, 2 });

            migrationBuilder.InsertData(
                table: "QuestStages",
                columns: new[] { "QuestId", "Sequence", "DescriptionTextId" },
                values: new object[] { 6L, 0, null });

            migrationBuilder.InsertData(
                table: "QuestTemplates",
                columns: new[] { "Id", "ClassRequirement", "CompletionTextId", "DescriptionTextId", "EnderCreatureId", "Environment", "GiverCreatureId", "IsRepeatable", "LevelRequirement", "Rarity", "RepeatFrequency", "RequiredQuestId", "RewardExperience", "RewardMoney", "ScriptName", "TitleTextId", "Type" },
                values: new object[] { 7L, null, 57, 56, 2m, 0, 2m, false, 5, 0, null, 6L, 700L, 400m, null, 55, 0 });

            migrationBuilder.InsertData(
                table: "QuestItemDrops",
                columns: new[] { "CreatureTemplateId", "ObjectiveId", "Chance" },
                values: new object[] { 5m, 501L, 50f });

            migrationBuilder.InsertData(
                table: "QuestItemRewards",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[] { 64m, 7L, 1L });

            migrationBuilder.InsertData(
                table: "QuestObjectives",
                columns: new[] { "Id", "Count", "CreatureTemplateId", "DescriptionTextId", "ItemTemplateId", "QuestId", "StageSequence", "Type" },
                values: new object[,]
                {
                    { 601L, 8L, 7m, 37, null, 6L, 0, 1 },
                    { 602L, 3L, null, 54, 61m, 6L, 0, 2 }
                });

            migrationBuilder.InsertData(
                table: "QuestStages",
                columns: new[] { "QuestId", "Sequence", "DescriptionTextId" },
                values: new object[] { 7L, 0, null });

            migrationBuilder.InsertData(
                table: "QuestTemplates",
                columns: new[] { "Id", "ClassRequirement", "CompletionTextId", "DescriptionTextId", "EnderCreatureId", "Environment", "GiverCreatureId", "IsRepeatable", "LevelRequirement", "Rarity", "RepeatFrequency", "RequiredQuestId", "RewardExperience", "RewardMoney", "ScriptName", "TitleTextId", "Type" },
                values: new object[] { 8L, null, 61, 60, 1m, 0, 1m, false, 7, 0, null, 7L, 1500L, 1000m, null, 59, 0 });

            migrationBuilder.InsertData(
                table: "QuestItemDrops",
                columns: new[] { "CreatureTemplateId", "ObjectiveId", "Chance" },
                values: new object[] { 7m, 602L, 35f });

            migrationBuilder.InsertData(
                table: "QuestItemRewards",
                columns: new[] { "ItemTemplateId", "QuestId", "Count" },
                values: new object[] { 65m, 8L, 1L });

            migrationBuilder.InsertData(
                table: "QuestObjectives",
                columns: new[] { "Id", "Count", "CreatureTemplateId", "DescriptionTextId", "ItemTemplateId", "QuestId", "StageSequence", "Type" },
                values: new object[] { 701L, 1L, null, 58, 62m, 7L, 0, 2 });

            migrationBuilder.InsertData(
                table: "QuestStages",
                columns: new[] { "QuestId", "Sequence", "DescriptionTextId" },
                values: new object[,]
                {
                    { 8L, 0, 65 },
                    { 8L, 1, 66 }
                });

            migrationBuilder.InsertData(
                table: "QuestItemDrops",
                columns: new[] { "CreatureTemplateId", "ObjectiveId", "Chance" },
                values: new object[] { 9m, 701L, 100f });

            migrationBuilder.InsertData(
                table: "QuestObjectives",
                columns: new[] { "Id", "Count", "CreatureTemplateId", "DescriptionTextId", "ItemTemplateId", "QuestId", "StageSequence", "Type" },
                values: new object[,]
                {
                    { 801L, 1L, 12m, 62, null, 8L, 0, 3 },
                    { 802L, 2L, 8m, 63, null, 8L, 1, 1 },
                    { 803L, 1L, null, 64, 63m, 8L, 1, 2 }
                });

            migrationBuilder.InsertData(
                table: "QuestItemDrops",
                columns: new[] { "CreatureTemplateId", "ObjectiveId", "Chance" },
                values: new object[] { 10m, 803L, 100f });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 43 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 44 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 45 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 46 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 47 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 48 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 49 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 50 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 51 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 52 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 53 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 54 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 55 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 56 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 57 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 58 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 59 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 60 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 61 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 62 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 63 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 64 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 65 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 66 });

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 43);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 44);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 45);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 46);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 47);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 48);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 49);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 50);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 51);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 52);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 53);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 54);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 55);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 56);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 57);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 58);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 59);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 60);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 61);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 62);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 63);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 64);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 65);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 66);

            migrationBuilder.DeleteData(
                table: "QuestItemDrops",
                keyColumns: new[] { "CreatureTemplateId", "ObjectiveId" },
                keyValues: new object[] { 6m, 401L });

            migrationBuilder.DeleteData(
                table: "QuestItemDrops",
                keyColumns: new[] { "CreatureTemplateId", "ObjectiveId" },
                keyValues: new object[] { 5m, 501L });

            migrationBuilder.DeleteData(
                table: "QuestItemDrops",
                keyColumns: new[] { "CreatureTemplateId", "ObjectiveId" },
                keyValues: new object[] { 7m, 602L });

            migrationBuilder.DeleteData(
                table: "QuestItemDrops",
                keyColumns: new[] { "CreatureTemplateId", "ObjectiveId" },
                keyValues: new object[] { 9m, 701L });

            migrationBuilder.DeleteData(
                table: "QuestItemDrops",
                keyColumns: new[] { "CreatureTemplateId", "ObjectiveId" },
                keyValues: new object[] { 10m, 803L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 2m, 4L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 56m, 6L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 64m, 7L });

            migrationBuilder.DeleteData(
                table: "QuestItemRewards",
                keyColumns: new[] { "ItemTemplateId", "QuestId" },
                keyValues: new object[] { 65m, 8L });

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 601L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 801L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 802L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 401L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 501L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 602L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 701L);

            migrationBuilder.DeleteData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 803L);

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 8L, 0 });

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 4L, 0 });

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 5L, 0 });

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 6L, 0 });

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 7L, 0 });

            migrationBuilder.DeleteData(
                table: "QuestStages",
                keyColumns: new[] { "QuestId", "Sequence" },
                keyValues: new object[] { 8L, 1 });

            migrationBuilder.DeleteData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 8L);

            migrationBuilder.DeleteData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 7L);

            migrationBuilder.DeleteData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 6L);

            migrationBuilder.DeleteData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 5L);

            migrationBuilder.DeleteData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 4L);
        }
    }
}
