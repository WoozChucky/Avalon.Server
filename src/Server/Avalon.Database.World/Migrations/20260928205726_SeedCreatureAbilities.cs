using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedCreatureAbilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AbilityTemplates",
                columns: new[] { "Id", "Affects", "AimMode", "AllowedClasses", "Anchor", "AnimationId", "ArcDegrees", "BaseDamageCoefficient", "CastTime", "Cooldown", "Cost", "EffectValue", "Effects", "Flags", "HealThreatPerHp", "Name", "Pierce", "PowerGainPerHit", "ProjectileSpeed", "Radius", "Range", "Reach", "ScalingCoefficient", "ScalingStat", "ScriptName", "Shape", "TauntDurationMs", "ThreatMultiplier" },
                values: new object[,]
                {
                    { 300L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 90f, 1f, 0L, 2250L, 0L, 0L, 1, 0L, 0f, "Gore", false, 0, 0f, 0f, 2, 1.8f, 0f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 301L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 0f, 1.6f, 0L, 10000L, 0L, 0L, 1, 0L, 0f, "Trample", false, 0, 0f, 2.5f, 5, 0f, 0f, (byte)0, "CircleAbilityScript", (byte)0, 0L, 1f },
                    { 302L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 90f, 1f, 0L, 2250L, 0L, 0L, 1, 0L, 0f, "Bite", false, 0, 0f, 0f, 2, 1.8f, 0f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 303L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 90f, 1.8f, 0L, 8000L, 0L, 0L, 1, 0L, 0f, "Ravenous Claw", false, 0, 0f, 0f, 5, 2.5f, 0f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 304L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 90f, 1f, 0L, 2250L, 0L, 0L, 1, 0L, 0f, "Sting", false, 0, 0f, 0f, 2, 1.5f, 0f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 305L, (byte)0, (byte)1, new int[0], (byte)0, 0L, 0f, 1.4f, 0L, 6000L, 0L, 0L, 1, 0L, 0f, "Blight Spit", false, 0, 14f, 0f, 10, 10f, 0f, (byte)0, "ProjectileAbilityScript", (byte)2, 0L, 1f },
                    { 306L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 90f, 1f, 0L, 2250L, 0L, 0L, 1, 0L, 0f, "Slam", false, 0, 0f, 0f, 2, 1.8f, 0f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 307L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 0f, 1.5f, 0L, 12000L, 0L, 0L, 1, 0L, 0f, "Rotting Burst", false, 0, 0f, 3f, 5, 0f, 0f, (byte)0, "CircleAbilityScript", (byte)0, 0L, 1f },
                    { 308L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 90f, 1f, 0L, 2250L, 0L, 0L, 1, 0L, 0f, "Maul", false, 0, 0f, 0f, 2, 2f, 0f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 309L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 100f, 1.8f, 0L, 9000L, 0L, 0L, 1, 0L, 0f, "Rending Frenzy", false, 0, 0f, 0f, 5, 2.5f, 0f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 310L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 0f, 2f, 1000L, 15000L, 0L, 0L, 1, 0L, 0f, "Howling Roar", false, 0, 0f, 5f, 5, 0f, 0f, (byte)0, "CircleAbilityScript", (byte)0, 0L, 1f },
                    { 311L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 90f, 1f, 0L, 2250L, 0L, 0L, 1, 0L, 0f, "Tusk Gore", false, 0, 0f, 0f, 2, 2f, 0f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 312L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 60f, 2.4f, 1200L, 14000L, 0L, 0L, 1, 0L, 0f, "Earthsplitter", false, 0, 0f, 0f, 5, 5f, 0f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 313L, (byte)0, (byte)1, new int[0], (byte)0, 0L, 0f, 1.6f, 0L, 10000L, 0L, 0L, 1, 0L, 0f, "Thorn Volley", true, 0, 16f, 0f, 20, 12f, 0f, (byte)0, "ProjectileAbilityScript", (byte)2, 0L, 1f },
                    { 314L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 90f, 1f, 0L, 2250L, 0L, 0L, 1, 0L, 0f, "Bramble Lash", false, 0, 0f, 0f, 5, 2.5f, 0f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 315L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 0f, 2.5f, 1200L, 16000L, 0L, 0L, 1, 0L, 0f, "Bramble Nova", false, 0, 0f, 6f, 10, 0f, 0f, (byte)0, "CircleAbilityScript", (byte)0, 0L, 1f },
                    { 316L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 120f, 1.8f, 0L, 8000L, 0L, 0L, 1, 0L, 0f, "Thornspray", false, 0, 0f, 0f, 5, 5f, 0f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f }
                });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "ScriptName",
                value: "ThornbackBoarScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "ScriptName",
                value: "GreyFenWolfScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "ScriptName",
                value: "BlightflySwarmlingScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "ScriptName",
                value: "HuskOfTheWoldScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "ScriptName",
                value: "BramblemawAlphaScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "ScriptName",
                value: "OldTuskrootScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "ScriptName",
                value: "MotherBrambleScript");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 300L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 301L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 302L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 303L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 304L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 305L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 306L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 307L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 308L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 309L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 310L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 311L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 312L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 313L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 314L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 315L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 316L);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "ScriptName",
                value: "AggroDefendScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "ScriptName",
                value: "AggroDefendScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "ScriptName",
                value: "AggroDefendScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "ScriptName",
                value: "AggroDefendScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "ScriptName",
                value: "AggroDefendScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "ScriptName",
                value: "AggroDefendScript");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "ScriptName",
                value: "AggroDefendScript");
        }
    }
}
