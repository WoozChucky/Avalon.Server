using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedTownVendors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-ordered parents first (#432): texts, items and creatures, then what points at them, so the foreign keys accept every row on Postgres. Down deletes in the exact reverse.
            migrationBuilder.InsertData(
                table: "LocalizedTexts",
                columns: new[] { "Id", "Text" },
                values: new object[,]
                {
                    { 17, "Steel, stave or string, traveller. What'll it be?" },
                    { 18, "Every blade here I hammered myself. Won't match what the forest spits out, but it'll keep you breathing till you find better." },
                    { 19, "Mind the rack. Looking to cover something?" },
                    { 20, "Plate, leather, cloth. I fit every trade. Buy it plain, earn it fancy." },
                    { 21, "Potions, scrolls, supplies. And I'll take what you've no use for." },
                    { 22, "I buy anything that isn't nailed to you. Fair prices, mostly." },
                    { 23, "What do you deal in?" },
                    { 24, "Show me your wares." }
                });

            migrationBuilder.InsertData(
                table: "LocalizedTextLocales",
                columns: new[] { "Locale", "TextId", "Text" },
                values: new object[,]
                {
                    { "ptPT", 17, "Aço, cajado ou corda, viajante. O que vai ser?" },
                    { "ptPT", 18, "Cada lâmina aqui fui eu que a forjei. Não se compara ao que a floresta cospe, mas há de te manter com vida até encontrares melhor." },
                    { "ptPT", 19, "Cuidado com o expositor. Queres cobrir alguma coisa?" },
                    { "ptPT", 20, "Placas, couro, tecido. Visto todos os ofícios. Compra-o simples, ganha-o vistoso." },
                    { "ptPT", 21, "Poções, pergaminhos, mantimentos. E fico com o que não te faz falta." },
                    { "ptPT", 22, "Compro tudo o que não estiver pregado a ti. Preços justos, quase sempre." },
                    { "ptPT", 23, "Com que é que negoceias?" },
                    { "ptPT", 24, "Mostra-me a tua mercadoria." }
                });

            migrationBuilder.InsertData(
                table: "ItemTemplates",
                columns: new[] { "Id", "AllowedClasses", "BuyPrice", "Class", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2", "DisplayId", "Flags", "ItemPower", "MaxStackSize", "Name", "Rarity", "RequiredLevel", "SellPrice", "Slot", "StatType1", "StatType10", "StatType2", "StatType3", "StatType4", "StatType5", "StatType6", "StatType7", "StatType8", "StatType9", "StatValue1", "StatValue10", "StatValue2", "StatValue3", "StatValue4", "StatValue5", "StatValue6", "StatValue7", "StatValue8", "StatValue9", "SubClass" },
                values: new object[,]
                {
                    { 32m, "Warrior", 120L, 1, 2L, null, 1L, null, 0, null, 32L, 0, 2, 1L, "Ironwood Sword", 1, 1, 30L, 9, 12, null, 1, null, null, null, null, null, null, null, 13L, null, 1L, null, null, null, null, null, null, null, 100 },
                    { 33m, "Wizard", 120L, 1, 3L, null, 1L, null, 0, null, 33L, 0, 2, 1L, "Ash Staff", 1, 1, 30L, 9, 12, null, 3, null, null, null, null, null, null, null, 18L, null, 1L, null, null, null, null, null, null, null, 101 },
                    { 34m, "Hunter", 120L, 1, 2L, null, 1L, null, 0, null, 34L, 0, 2, 1L, "Hunter's Shortbow", 1, 1, 30L, 9, 12, null, 2, null, null, null, null, null, null, null, 15L, null, 1L, null, null, null, null, null, null, null, 102 },
                    { 35m, "Healer", 120L, 1, 2L, null, 1L, null, 0, null, 35L, 0, 2, 1L, "Oak Mace", 1, 1, 30L, 9, 12, null, 3, null, null, null, null, null, null, null, 15L, null, 1L, null, null, null, null, null, null, null, 100 },
                    { 36m, "Warrior", 60L, 2, null, null, null, null, null, null, 36L, 0, 2, 1L, "Ironbound Helm", 1, 1, 15L, 0, 1, null, 4, 0, null, null, null, null, null, null, 1L, null, 2L, 1L, null, null, null, null, null, null, 201 },
                    { 37m, "Warrior", 100L, 2, null, null, null, null, null, null, 37L, 0, 2, 1L, "Ironbound Chestguard", 1, 1, 25L, 3, 1, null, 4, 0, null, null, null, null, null, null, 1L, null, 5L, 1L, null, null, null, null, null, null, 202 },
                    { 38m, "Warrior", 80L, 2, null, null, null, null, null, null, 38L, 0, 2, 1L, "Ironbound Legguards", 1, 1, 20L, 5, 1, null, 4, 0, null, null, null, null, null, null, 1L, null, 4L, 1L, null, null, null, null, null, null, 203 },
                    { 39m, "Warrior", 40L, 2, null, null, null, null, null, null, 39L, 0, 2, 1L, "Ironbound Gauntlets", 1, 1, 10L, 4, 1, null, 4, 0, null, null, null, null, null, null, 1L, null, 2L, 1L, null, null, null, null, null, null, 205 },
                    { 40m, "Warrior", 40L, 2, null, null, null, null, null, null, 40L, 0, 2, 1L, "Ironbound Boots", 1, 1, 10L, 6, 1, null, 4, 0, null, null, null, null, null, null, 1L, null, 2L, 1L, null, null, null, null, null, null, 204 },
                    { 41m, "Wizard", 60L, 2, null, null, null, null, null, null, 41L, 0, 2, 1L, "Linen Hood", 1, 1, 15L, 0, 3, null, 4, null, null, null, null, null, null, null, 1L, null, 1L, null, null, null, null, null, null, null, 201 },
                    { 42m, "Wizard", 100L, 2, null, null, null, null, null, null, 42L, 0, 2, 1L, "Linen Robe", 1, 1, 25L, 3, 3, null, 4, null, null, null, null, null, null, null, 2L, null, 2L, null, null, null, null, null, null, null, 202 },
                    { 43m, "Wizard", 80L, 2, null, null, null, null, null, null, 43L, 0, 2, 1L, "Linen Leggings", 1, 1, 20L, 5, 3, null, 4, null, null, null, null, null, null, null, 2L, null, 1L, null, null, null, null, null, null, null, 203 },
                    { 44m, "Wizard", 40L, 2, null, null, null, null, null, null, 44L, 0, 2, 1L, "Linen Gloves", 1, 1, 10L, 4, 3, null, 4, null, null, null, null, null, null, null, 1L, null, 1L, null, null, null, null, null, null, null, 205 },
                    { 45m, "Wizard", 40L, 2, null, null, null, null, null, null, 45L, 0, 2, 1L, "Linen Slippers", 1, 1, 10L, 6, 3, null, 4, null, null, null, null, null, null, null, 1L, null, 1L, null, null, null, null, null, null, null, 204 },
                    { 46m, "Hunter", 60L, 2, null, null, null, null, null, null, 46L, 0, 2, 1L, "Hide Cap", 1, 1, 15L, 0, 2, null, 4, null, null, null, null, null, null, null, 1L, null, 1L, null, null, null, null, null, null, null, 201 },
                    { 47m, "Hunter", 100L, 2, null, null, null, null, null, null, 47L, 0, 2, 1L, "Hide Jerkin", 1, 1, 25L, 3, 2, null, 4, null, null, null, null, null, null, null, 2L, null, 3L, null, null, null, null, null, null, null, 202 },
                    { 48m, "Hunter", 80L, 2, null, null, null, null, null, null, 48L, 0, 2, 1L, "Hide Breeches", 1, 1, 20L, 5, 2, null, 4, null, null, null, null, null, null, null, 2L, null, 2L, null, null, null, null, null, null, null, 203 },
                    { 49m, "Hunter", 40L, 2, null, null, null, null, null, null, 49L, 0, 2, 1L, "Hide Grips", 1, 1, 10L, 4, 2, null, 4, null, null, null, null, null, null, null, 1L, null, 1L, null, null, null, null, null, null, null, 205 },
                    { 50m, "Hunter", 40L, 2, null, null, null, null, null, null, 50L, 0, 2, 1L, "Hide Boots", 1, 1, 10L, 6, 2, null, 4, null, null, null, null, null, null, null, 1L, null, 1L, null, null, null, null, null, null, null, 204 },
                    { 51m, "Healer", 60L, 2, null, null, null, null, null, null, 51L, 0, 2, 1L, "Wool Circlet", 1, 1, 15L, 0, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 1L, null, null, null, null, null, null, 201 },
                    { 52m, "Healer", 100L, 2, null, null, null, null, null, null, 52L, 0, 2, 1L, "Wool Vestments", 1, 1, 25L, 3, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 2L, null, null, null, null, null, null, 202 },
                    { 53m, "Healer", 80L, 2, null, null, null, null, null, null, 53L, 0, 2, 1L, "Wool Leggings", 1, 1, 20L, 5, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 1L, null, null, null, null, null, null, 203 },
                    { 54m, "Healer", 40L, 2, null, null, null, null, null, null, 54L, 0, 2, 1L, "Wool Handwraps", 1, 1, 10L, 4, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 1L, null, null, null, null, null, null, 205 },
                    { 55m, "Healer", 40L, 2, null, null, null, null, null, null, 55L, 0, 2, 1L, "Wool Sandals", 1, 1, 10L, 6, 3, null, 0, 4, null, null, null, null, null, null, 1L, null, 1L, 1L, null, null, null, null, null, null, 204 },
                    { 56m, "Warrior,Wizard,Hunter,Healer", 25L, 0, null, null, null, null, null, null, 56L, 256, null, 40L, "Greater Health Potion", 1, null, 12L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 0 }
                });

            migrationBuilder.InsertData(
                table: "CreatureTemplates",
                columns: new[] { "Id", "AIName", "ArmorModifier", "BaseAttackTime", "BodyRemoveTimerSecs", "DamageModifier", "DetectionRange", "DmgSchool", "Exp", "ExperienceModifier", "Family", "HealthModifier", "IconName", "Invulnerable", "LootTableId", "ManaModifier", "MaxGold", "MaxLevel", "MinGold", "MinLevel", "MovementId", "MovementType", "Name", "RangeAttackTime", "Rarity", "RegenHealth", "RespawnTimerSecs", "ScriptName", "SpeedRun", "SpeedSwim", "SpeedWalk", "SubName", "Type" },
                values: new object[,]
                {
                    { 12m, "", 1f, 1, 10, 1f, 20f, (short)0, 0L, 1f, 0, 1f, "", true, null, 1f, 0, (short)1, 0, (short)1, 0, (short)0, "Garrick Emberforge", 0, 0, (short)1, 180, "TownNpcScript", 5f, 1.6f, 2f, "Weapons Dealer", 7 },
                    { 13m, "", 1f, 1, 10, 1f, 20f, (short)0, 0L, 1f, 0, 1f, "", true, null, 1f, 0, (short)1, 0, (short)1, 0, (short)0, "Hilde Brassbuckle", 0, 0, (short)1, 180, "TownNpcScript", 5f, 1.6f, 2f, "Armourer", 7 },
                    { 14m, "", 1f, 1, 10, 1f, 20f, (short)0, 0L, 1f, 0, 1f, "", true, null, 1f, 0, (short)1, 0, (short)1, 0, (short)0, "Tobin Marrowfield", 0, 0, (short)1, 180, "TownNpcScript", 5f, 1.6f, 2f, "Trade Goods", 7 }
                });

            migrationBuilder.InsertData(
                table: "DialogueNodes",
                columns: new[] { "Id", "CreatureTemplateId", "IsRoot", "TextId" },
                values: new object[,]
                {
                    { 8, 12m, true, 17 },
                    { 9, 12m, false, 18 },
                    { 10, 13m, true, 19 },
                    { 11, 13m, false, 20 },
                    { 12, 14m, true, 21 },
                    { 13, 14m, false, 22 }
                });

            migrationBuilder.InsertData(
                table: "DialogueOptions",
                columns: new[] { "Id", "Action", "NextNodeId", "NodeId", "SortOrder", "TextId" },
                values: new object[,]
                {
                    { 12, null, 9, 8, (short)0, 23 },
                    { 13, 1, 8, 8, (short)1, 24 },
                    { 14, null, null, 8, (short)2, 10 },
                    { 15, 1, 9, 9, (short)0, 24 },
                    { 16, null, null, 9, (short)1, 10 },
                    { 17, null, 11, 10, (short)0, 23 },
                    { 18, 1, 10, 10, (short)1, 24 },
                    { 19, null, null, 10, (short)2, 10 },
                    { 20, 1, 11, 11, (short)0, 24 },
                    { 21, null, null, 11, (short)1, 10 },
                    { 22, null, 13, 12, (short)0, 23 },
                    { 23, 1, 12, 12, (short)1, 24 },
                    { 24, null, null, 12, (short)2, 10 },
                    { 25, 1, 13, 13, (short)0, 24 },
                    { 26, null, null, 13, (short)1, 10 }
                });

            migrationBuilder.InsertData(
                table: "MapCreatureSpawns",
                columns: new[] { "Id", "CreatureTemplateId", "Facing", "MapTemplateId", "OffsetX", "OffsetY", "OffsetZ", "PathId" },
                values: new object[,]
                {
                    { 5, 12m, 114f, 1, -9f, 0f, 4f, null },
                    { 6, 13m, 132f, 1, -9f, 0f, 8f, null },
                    { 7, 14m, 149f, 1, -6f, 0f, 10f, null }
                });

            migrationBuilder.InsertData(
                table: "VendorStocks",
                columns: new[] { "Id", "CreatureTemplateId", "ItemTemplateId", "MaxStock", "PriceOverride", "RequiredQuestId", "RequiredQuestState", "RestockSeconds", "Sequence" },
                values: new object[,]
                {
                    { 1, 12m, 32m, null, null, null, null, null, 1L },
                    { 2, 12m, 33m, null, null, null, null, null, 2L },
                    { 3, 12m, 34m, null, null, null, null, null, 3L },
                    { 4, 12m, 35m, null, null, null, null, null, 4L },
                    { 5, 13m, 36m, null, null, null, null, null, 1L },
                    { 6, 13m, 37m, 2L, null, null, null, 1800L, 2L },
                    { 7, 13m, 38m, null, null, null, null, null, 3L },
                    { 8, 13m, 39m, null, null, null, null, null, 4L },
                    { 9, 13m, 40m, null, null, null, null, null, 5L },
                    { 10, 13m, 41m, null, null, null, null, null, 6L },
                    { 11, 13m, 42m, 2L, null, null, null, 1800L, 7L },
                    { 12, 13m, 43m, null, null, null, null, null, 8L },
                    { 13, 13m, 44m, null, null, null, null, null, 9L },
                    { 14, 13m, 45m, null, null, null, null, null, 10L },
                    { 15, 13m, 46m, null, null, null, null, null, 11L },
                    { 16, 13m, 47m, 2L, null, null, null, 1800L, 12L },
                    { 17, 13m, 48m, null, null, null, null, null, 13L },
                    { 18, 13m, 49m, null, null, null, null, null, 14L },
                    { 19, 13m, 50m, null, null, null, null, null, 15L },
                    { 20, 13m, 51m, null, null, null, null, null, 16L },
                    { 21, 13m, 52m, 2L, null, null, null, 1800L, 17L },
                    { 22, 13m, 53m, null, null, null, null, null, 18L },
                    { 23, 13m, 54m, null, null, null, null, null, 19L },
                    { 24, 13m, 55m, null, null, null, null, null, 20L },
                    { 25, 14m, 1m, null, null, null, null, null, 1L },
                    { 26, 14m, 2m, null, null, null, null, null, 2L },
                    { 27, 14m, 3m, null, null, null, null, null, 3L },
                    { 28, 14m, 9m, null, null, null, null, null, 4L },
                    { 29, 14m, 10m, null, null, null, null, null, 5L },
                    { 30, 14m, 11m, null, null, null, null, null, 6L },
                    { 31, 14m, 56m, 5L, null, null, null, 600L, 7L }
                });

            migrationBuilder.InsertData(
                table: "VendorStockCosts",
                columns: new[] { "ItemTemplateId", "VendorStockId", "Count" },
                values: new object[] { 1m, 31, 2L });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "VendorStockCosts",
                keyColumns: new[] { "ItemTemplateId", "VendorStockId" },
                keyValues: new object[] { 1m, 31 });

            migrationBuilder.DeleteData(
                table: "VendorStocks",
                keyColumn: "Id",
                keyValues: new object[] { 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 });

            migrationBuilder.DeleteData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValues: new object[] { 7, 6, 5 });

            migrationBuilder.DeleteData(
                table: "DialogueOptions",
                keyColumn: "Id",
                keyValues: new object[] { 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12 });

            migrationBuilder.DeleteData(
                table: "DialogueNodes",
                keyColumn: "Id",
                keyValues: new object[] { 13, 12, 11, 10, 9, 8 });

            migrationBuilder.DeleteData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValues: new object[] { 14m, 13m, 12m });

            migrationBuilder.DeleteData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValues: new object[] { 56m, 55m, 54m, 53m, 52m, 51m, 50m, 49m, 48m, 47m, 46m, 45m, 44m, 43m, 42m, 41m, 40m, 39m, 38m, 37m, 36m, 35m, 34m, 33m, 32m });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[,]
                {
                    { "ptPT", 24 },
                    { "ptPT", 23 },
                    { "ptPT", 22 },
                    { "ptPT", 21 },
                    { "ptPT", 20 },
                    { "ptPT", 19 },
                    { "ptPT", 18 },
                    { "ptPT", 17 }
                });

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValues: new object[] { 24, 23, 22, 21, 20, 19, 18, 17 });
        }
    }
}
