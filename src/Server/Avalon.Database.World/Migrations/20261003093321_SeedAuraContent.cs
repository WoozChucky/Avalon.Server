using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedAuraContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AuraTemplates",
                columns: new[] { "Id", "BaseDamageCoefficient", "DurationMs", "Icon", "Kind", "MaxStacks", "Name", "PeriodicBase", "PeriodicKind", "ScalingCoefficient", "ScalingStat", "ScriptName", "Stacking", "TickIntervalMs" },
                values: new object[,]
                {
                    { 1L, 0f, 12000L, "bleed", (byte)2, 3L, "Bleed", 12f, (byte)1, 0.25f, (byte)0, null, (byte)2, 3000L },
                    { 2L, 0f, 9000L, "burn", (byte)2, 1L, "Burn", 24f, (byte)1, 0.6f, (byte)1, null, (byte)1, 3000L },
                    { 3L, 0f, 6000L, "crippled", (byte)2, 1L, "Crippled", 0f, (byte)0, 0f, (byte)0, null, (byte)1, 0L },
                    { 4L, 0f, 12000L, "renew", (byte)1, 1L, "Renew", 24f, (byte)2, 0.4f, (byte)1, null, (byte)1, 3000L },
                    { 5L, 0f, 30000L, "fortified", (byte)1, 1L, "Fortified", 0f, (byte)0, 0f, (byte)0, null, (byte)1, 0L },
                    { 6L, 1f, 9000L, "poison", (byte)2, 3L, "Poison", 3f, (byte)1, 0f, (byte)0, null, (byte)2, 3000L },
                    { 7L, 0f, 10000L, "sundered", (byte)2, 1L, "Sundered", 0f, (byte)0, 0f, (byte)0, null, (byte)1, 0L }
                });

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 1,
                column: "StartingSpells",
                value: "200,201,202,203");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 2,
                column: "StartingSpells",
                value: "210,211,212,213");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 3,
                column: "StartingSpells",
                value: "220,221,222,223");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 4,
                column: "StartingSpells",
                value: "230,231,232,233,234");

            migrationBuilder.InsertData(
                table: "AbilityTemplates",
                columns: new[] { "Id", "Affects", "AimMode", "AllowedClasses", "Anchor", "AnimationId", "ArcDegrees", "AuraId", "BaseDamageCoefficient", "CastTime", "Cooldown", "Cost", "CostPowerType", "EffectValue", "Effects", "Flags", "HealThreatPerHp", "Name", "Pierce", "PowerGainPerHit", "ProjectileSpeed", "Radius", "Range", "Reach", "ScalingCoefficient", "ScalingStat", "ScriptName", "Shape", "TauntDurationMs", "ThreatMultiplier" },
                values: new object[,]
                {
                    { 203L, (byte)0, (byte)0, new[] { 1 }, (byte)0, 0L, 90f, 1L, 0.5f, 0L, 6000L, 10L, 2, 8L, 1, 0L, 0f, "Rend", false, 0, 0f, 0f, 2, 2.5f, 0.2f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 213L, (byte)0, (byte)1, new[] { 2 }, (byte)1, 0L, 0f, 2L, 0f, 0L, 6000L, 20L, 1, 0L, 8, 0L, 0f, "Ignite", false, 0, 0f, 3f, 20, 18f, 0f, (byte)1, "CircleAbilityScript", (byte)0, 0L, 1f },
                    { 223L, (byte)0, (byte)1, new[] { 3 }, (byte)0, 0L, 0f, 3L, 0.6f, 0L, 8000L, 15L, 3, 14L, 1, 0L, 0f, "Crippling Shot", false, 0, 28f, 0f, 20, 25f, 0.35f, (byte)0, "ProjectileAbilityScript", (byte)2, 0L, 1f },
                    { 233L, (byte)1, (byte)1, new[] { 4 }, (byte)1, 0L, 0f, 4L, 0f, 0L, 6000L, 15L, 1, 0L, 4, 0L, 0.5f, "Renew", false, 0, 0f, 4f, 10, 15f, 0f, (byte)1, "CircleAbilityScript", (byte)0, 0L, 1f },
                    { 234L, (byte)1, (byte)0, new[] { 4 }, (byte)0, 0L, 0f, 5L, 0f, 0L, 20000L, 20L, 1, 0L, 4, 0L, 0.5f, "Fortify", false, 0, 0f, 8f, 5, 0f, 0f, (byte)1, "CircleAbilityScript", (byte)0, 0L, 1f },
                    { 317L, (byte)0, (byte)1, new int[0], (byte)0, 0L, 0f, 6L, 0.6f, 0L, 8000L, 0L, 0, 0L, 1, 0L, 0f, "Venom Spit", false, 0, 14f, 0f, 10, 10f, 0f, (byte)0, "ProjectileAbilityScript", (byte)2, 0L, 1f },
                    { 318L, (byte)0, (byte)0, new int[0], (byte)0, 0L, 0f, 7L, 0f, 0L, 18000L, 0L, 0, 0L, 8, 0L, 0f, "Sundering Howl", false, 0, 0f, 6f, 10, 0f, 0f, (byte)0, "CircleAbilityScript", (byte)0, 0L, 1f }
                });

            migrationBuilder.InsertData(
                table: "AuraStatModifiers",
                columns: new[] { "AuraId", "Stat", "Kind", "Value" },
                values: new object[,]
                {
                    { 3L, (byte)8, (byte)1, -30f },
                    { 5L, (byte)1, (byte)2, 20f },
                    { 7L, (byte)1, (byte)2, -25f }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 203L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 213L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 223L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 233L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 234L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 317L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 318L);

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 3L, (byte)8 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 5L, (byte)1 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 7L, (byte)1 });

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 4L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 5L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 6L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 7L);

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 1,
                column: "StartingSpells",
                value: "200,201,202");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 2,
                column: "StartingSpells",
                value: "210,211,212");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 3,
                column: "StartingSpells",
                value: "220,221,222");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 4,
                column: "StartingSpells",
                value: "230,231,232");
        }
    }
}
