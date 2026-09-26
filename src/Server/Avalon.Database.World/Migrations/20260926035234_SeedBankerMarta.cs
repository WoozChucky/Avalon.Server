using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedBankerMarta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-ordered parents first (#463): texts, then what points at them, so the foreign keys
            // accept every row on Postgres. Down deletes in the exact reverse.
            migrationBuilder.InsertData(
                table: "LocalizedTexts",
                columns: new[] { "Id", "Text" },
                values: new object[,]
                {
                    { 15, "Coin and keepsakes both, {name}. The vault keeps what the road would take." },
                    { 16, "Open my bank." }
                });

            migrationBuilder.InsertData(
                table: "LocalizedTextLocales",
                columns: new[] { "Locale", "TextId", "Text" },
                values: new object[,]
                {
                    { "ptPT", 15, "Moedas e recordações, {name}. O cofre guarda o que a estrada levaria." },
                    { "ptPT", 16, "Abre o meu cofre." }
                });

            migrationBuilder.InsertData(
                table: "CreatureTemplates",
                columns: new[] { "Id", "AIName", "ArmorModifier", "BaseAttackTime", "BodyRemoveTimerSecs", "DamageModifier", "DetectionRange", "DmgSchool", "Exp", "ExperienceModifier", "Family", "HealthModifier", "IconName", "Invulnerable", "LootTableId", "ManaModifier", "MaxGold", "MaxLevel", "MinGold", "MinLevel", "MovementId", "MovementType", "Name", "RangeAttackTime", "Rarity", "RegenHealth", "RespawnTimerSecs", "ScriptName", "SpeedRun", "SpeedSwim", "SpeedWalk", "SubName", "Type" },
                values: new object[] { 11m, "", 1f, 1, 10, 1f, 20f, (short)0, 0L, 1f, 0, 1f, "", true, null, 1f, 0, (short)1, 0, (short)1, 0, (short)0, "Marta Ledgerwell", 0, 0, (short)1, 180, "TownNpcScript", 5f, 1.6f, 2f, "Banker", 7 });

            migrationBuilder.InsertData(
                table: "DialogueNodes",
                columns: new[] { "Id", "CreatureTemplateId", "IsRoot", "TextId" },
                values: new object[] { 7, 11m, true, 15 });

            migrationBuilder.InsertData(
                table: "DialogueOptions",
                columns: new[] { "Id", "Action", "NextNodeId", "NodeId", "SortOrder", "TextId" },
                values: new object[,]
                {
                    { 10, 0, 7, 7, (short)0, 16 },
                    { 11, null, null, 7, (short)1, 10 }
                });

            migrationBuilder.InsertData(
                table: "MapCreatureSpawns",
                columns: new[] { "Id", "CreatureTemplateId", "Facing", "MapTemplateId", "OffsetX", "OffsetY", "OffsetZ", "PathId" },
                values: new object[] { 4, 11m, 135f, 1, -6f, 0f, 6f, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "DialogueOptions",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "DialogueOptions",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "DialogueNodes",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 11m);

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 16 });

            migrationBuilder.DeleteData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 15 });

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 15);
        }
    }
}
