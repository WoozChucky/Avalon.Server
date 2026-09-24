using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedForestRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            // Hand-written: the spawn table's rows are not model seed data, so EF cannot scaffold
            // replacing them. The four existing entries are the placeholders the forest was seeded with
            // — Uriel, Borin and the Innkeeper standing in as monsters — which the original migration's
            // own comment asked to be replaced "once authored". Deleting by SpawnTableId rather than by
            // Id so this holds even if the placeholder ids differ in a database seeded out of order.
            migrationBuilder.Sql(@"
                DELETE FROM ""SpawnTableEntry"" WHERE ""SpawnTableId"" = 1;

                INSERT INTO ""SpawnTableEntry""
                    (""Id"", ""SpawnTableId"", ""Tag"", ""CreatureId"", ""Weight"", ""MinCount"", ""MaxCount"")
                VALUES
                    (1, 1, 'pack',  4,  1.0, 2, 3),
                    (2, 1, 'pack',  5,  1.0, 2, 4),
                    (3, 1, 'pack',  6,  0.8, 3, 5),
                    (4, 1, 'pack',  7,  0.6, 1, 2),
                    (5, 1, 'pack',  8,  0.3, 1, 1),
                    (6, 1, 'rare',  9,  1.0, 1, 1),
                    (7, 1, 'boss', 10,  1.0, 1, 1);
            ");
            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "ScriptName",
                value: "CreatureIdleScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "ScriptName",
                value: "CreatureIdleScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "ScriptName",
                value: "CreatureIdleScript");

            migrationBuilder.InsertData(
                table: "CreatureTemplates",
                columns: new[] { "Id", "AIName", "ArmorModifier", "BaseAttackTime", "BodyRemoveTimerSecs", "DamageModifier", "DetectionRange", "DmgSchool", "Exp", "ExperienceModifier", "Family", "HealthModifier", "IconName", "LootId", "ManaModifier", "MaxGold", "MaxLevel", "MinGold", "MinLevel", "MovementId", "MovementType", "Name", "RangeAttackTime", "Rarity", "RegenHealth", "RespawnTimerSecs", "ScriptName", "SpeedRun", "SpeedSwim", "SpeedWalk", "SubName", "Type" },
                values: new object[,]
                {
                    { 4m, "", 1f, 1, 10, 1f, 12f, (short)0, null, 1f, 5, 1.1f, "", 0, 1f, 0, (short)3, 0, (short)1, 0, (short)0, "Thornback Boar", 0, 0, (short)1, 180, "AggroDefendScript", 4f, 1.6f, 2f, "gore-scarred", 1 },
                    { 5m, "", 1f, 1, 10, 1.1f, 18f, (short)0, null, 1f, 1, 1f, "", 0, 1f, 0, (short)4, 0, (short)2, 0, (short)0, "Grey Fen Wolf", 0, 0, (short)1, 180, "AggroDefendScript", 4f, 1.6f, 2f, "lean and patient", 1 },
                    { 6m, "", 1f, 1, 10, 0.7f, 8f, (short)0, null, 1f, 0, 0.6f, "", 0, 1f, 0, (short)2, 0, (short)1, 0, (short)0, "Blightfly Swarmling", 0, 0, (short)1, 180, "AggroDefendScript", 4f, 1.6f, 2f, "a drone of the bloom", 8 },
                    { 7m, "", 1f, 1, 10, 1f, 14f, (short)0, null, 1f, 0, 1.3f, "", 0, 1f, 0, (short)4, 0, (short)3, 0, (short)0, "Husk of the Wold", 0, 0, (short)1, 180, "AggroDefendScript", 4f, 1.6f, 2f, "what the wold leaves behind", 6 },
                    { 8m, "", 1f, 1, 10, 1.1f, 22f, (short)0, null, 1f, 1, 1f, "", 0, 1f, 0, (short)5, 0, (short)3, 0, (short)0, "Bramblemaw Alpha", 0, 1, (short)1, 180, "AggroDefendScript", 4f, 1.6f, 2f, "the pack's black heart", 1 },
                    { 9m, "", 1f, 1, 10, 1.1f, 20f, (short)0, null, 1f, 5, 1.2f, "", 0, 1f, 0, (short)5, 0, (short)4, 0, (short)0, "Old Tuskroot", 0, 2, (short)1, 180, "AggroDefendScript", 4f, 1.6f, 2f, "older than the rot", 1 },
                    { 10m, "", 1f, 1, 10, 1f, 26f, (short)0, null, 1f, 0, 1f, "", 0, 1f, 0, (short)5, 0, (short)5, 0, (short)0, "Mother Bramble", 0, 3, (short)1, 180, "AggroDefendScript", 4f, 1.6f, 2f, "rooted at the heart of the wold", 4 }
                });

            migrationBuilder.UpdateData(
                table: "MapTemplates",
                keyColumn: "Id",
                keyValue: 2,
                column: "MaxLevel",
                value: 5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            // Best-effort: restores the placeholder mix the forest had before this migration. If the
            // seven forest templates are gone by then these rows would dangle, which is why the roster's
            // own deletion below runs after.
            migrationBuilder.Sql(@"
                DELETE FROM ""SpawnTableEntry"" WHERE ""SpawnTableId"" = 1;

                INSERT INTO ""SpawnTableEntry""
                    (""Id"", ""SpawnTableId"", ""Tag"", ""CreatureId"", ""Weight"", ""MinCount"", ""MaxCount"")
                VALUES
                    (1, 1, 'pack', 2, 1.0, 1, 2),
                    (2, 1, 'pack', 3, 0.5, 1, 1),
                    (3, 1, 'rare', 3, 1.0, 1, 1),
                    (4, 1, 'boss', 1, 1.0, 1, 1);
            ");
            migrationBuilder.DeleteData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m);

            migrationBuilder.DeleteData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m);

            migrationBuilder.DeleteData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m);

            migrationBuilder.DeleteData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m);

            migrationBuilder.DeleteData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m);

            migrationBuilder.DeleteData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m);

            migrationBuilder.DeleteData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "ScriptName",
                value: "UrielTownPatrolScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "ScriptName",
                value: "UrielPathfinderScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "ScriptName",
                value: "");

            migrationBuilder.UpdateData(
                table: "MapTemplates",
                keyColumn: "Id",
                keyValue: 2,
                column: "MaxLevel",
                value: 10);
        }
    }
}
