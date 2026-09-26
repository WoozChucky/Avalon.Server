using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <summary>
    /// #164: the twelve-skill starter kit replaces abilities 1, 2 and 100-103, and each class starts with
    /// its three. Ordered by hand so each step reads after what it depends on: Up deletes the retired
    /// rows, inserts the kit, then points StartingSpells at it; Down points StartingSpells back first.
    /// StartingSpells is a comma-separated text column with no foreign key, so nothing refuses another
    /// order; the order is for the reader.
    /// </summary>
    public partial class SeedStarterSkillKit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 100L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 101L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 102L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 103L);

            migrationBuilder.InsertData(
                table: "AbilityTemplates",
                columns: new[] { "Id", "Affects", "AimMode", "AllowedClasses", "Anchor", "AnimationId", "ArcDegrees", "CastTime", "Cooldown", "Cost", "EffectValue", "Effects", "Flags", "HealThreatPerHp", "Name", "Pierce", "ProjectileSpeed", "Radius", "Range", "Reach", "Shape", "SpellScript", "TauntDurationMs", "ThreatMultiplier" },
                values: new object[,]
                {
                    { 200L, (byte)0, (byte)0, new[] { 1 }, (byte)0, 0L, 100f, 0L, 800L, 0L, 12L, 1, 0L, 0f, "Cleave", false, 0f, 0f, 2, 2.5f, (byte)1, "ConeAbilityScript", 0L, 1f },
                    { 201L, (byte)0, (byte)0, new[] { 1 }, (byte)0, 0L, 0f, 0L, 5000L, 20L, 25L, 1, 0L, 0f, "Ground Slam", false, 0f, 3f, 5, 0f, (byte)0, "CircleAbilityScript", 0L, 1f },
                    { 202L, (byte)0, (byte)1, new[] { 1 }, (byte)0, 0L, 0f, 0L, 3000L, 10L, 20L, 1, 0L, 0f, "Hurled Axe", false, 18f, 0f, 10, 15f, (byte)2, "ProjectileAbilityScript", 0L, 1f },
                    { 210L, (byte)0, (byte)1, new[] { 2 }, (byte)0, 0L, 0f, 0L, 800L, 0L, 12L, 1, 0L, 0f, "Arcane Bolt", false, 22f, 0f, 20, 20f, (byte)2, "ProjectileAbilityScript", 0L, 1f },
                    { 211L, (byte)0, (byte)1, new[] { 2 }, (byte)1, 0L, 0f, 600L, 5000L, 25L, 35L, 1, 0L, 0f, "Flame Burst", false, 0f, 3f, 20, 18f, (byte)0, "CircleAbilityScript", 0L, 1f },
                    { 212L, (byte)0, (byte)1, new[] { 2 }, (byte)0, 0L, 60f, 0L, 4000L, 15L, 22L, 1, 0L, 0f, "Frost Fan", false, 0f, 0f, 5, 6f, (byte)1, "ConeAbilityScript", 0L, 1f },
                    { 220L, (byte)0, (byte)1, new[] { 3 }, (byte)0, 0L, 0f, 0L, 800L, 0L, 12L, 1, 0L, 0f, "Quick Shot", false, 28f, 0f, 20, 25f, (byte)2, "ProjectileAbilityScript", 0L, 1f },
                    { 221L, (byte)0, (byte)1, new[] { 3 }, (byte)0, 0L, 0f, 0L, 4000L, 15L, 25L, 1, 0L, 0f, "Piercing Arrow", true, 24f, 0f, 20, 30f, (byte)2, "ProjectileAbilityScript", 0L, 1f },
                    { 222L, (byte)0, (byte)1, new[] { 3 }, (byte)0, 0L, 45f, 0L, 4000L, 20L, 22L, 1, 0L, 0f, "Scatter Shot", false, 0f, 0f, 10, 8f, (byte)1, "ConeAbilityScript", 0L, 1f },
                    { 230L, (byte)0, (byte)1, new[] { 4 }, (byte)0, 0L, 0f, 0L, 800L, 0L, 12L, 1, 0L, 0f, "Smite", false, 20f, 0f, 20, 18f, (byte)2, "ProjectileAbilityScript", 0L, 1f },
                    { 231L, (byte)0, (byte)0, new[] { 4 }, (byte)0, 0L, 0f, 0L, 5000L, 20L, 22L, 1, 0L, 0f, "Radiant Pulse", false, 0f, 4f, 5, 0f, (byte)0, "CircleAbilityScript", 0L, 1f },
                    { 232L, (byte)1, (byte)1, new[] { 4 }, (byte)1, 0L, 0f, 0L, 8000L, 25L, 40L, 2, 0L, 0.5f, "Mending Circle", false, 0f, 4f, 10, 15f, (byte)0, "CircleAbilityScript", 0L, 1f }
                });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 1,
                column: "StartingSpells",
                value: "1,2,100");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 2,
                column: "StartingSpells",
                value: "2,101");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 3,
                column: "StartingSpells",
                value: "102");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 4,
                column: "StartingSpells",
                value: "103");

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 201L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 202L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 211L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 212L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 221L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 222L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 231L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 232L);

            migrationBuilder.InsertData(
                table: "AbilityTemplates",
                columns: new[] { "Id", "Affects", "AimMode", "AllowedClasses", "Anchor", "AnimationId", "ArcDegrees", "CastTime", "Cooldown", "Cost", "EffectValue", "Effects", "Flags", "HealThreatPerHp", "Name", "Pierce", "ProjectileSpeed", "Radius", "Range", "Reach", "Shape", "SpellScript", "TauntDurationMs", "ThreatMultiplier" },
                values: new object[,]
                {
                    { 1L, (byte)0, (byte)0, new[] { 1 }, (byte)0, 0L, 0f, 0L, 2500L, 25L, 10L, 1, 0L, 0f, "Strike", false, 0f, 0f, 2, 0f, (byte)0, "StrikeAbilityScript", 0L, 1f },
                    { 2L, (byte)0, (byte)0, new[] { 1, 2 }, (byte)0, 0L, 0f, 2000L, 1000L, 10L, 10L, 1, 0L, 0f, "Fireball", false, 0f, 0f, 10, 0f, (byte)0, "FireballAbilityScript", 0L, 1f },
                    { 100L, (byte)0, (byte)0, new[] { 1 }, (byte)0, 0L, 0f, 0L, 500L, 0L, 15L, 1, 0L, 0f, "Warrior Slash", false, 0f, 0f, 2, 0f, (byte)0, "StrikeAbilityScript", 0L, 1.5f },
                    { 101L, (byte)0, (byte)0, new[] { 2 }, (byte)0, 0L, 0f, 200L, 700L, 0L, 8L, 1, 0L, 0f, "Wizard Bolt", false, 0f, 0f, 10, 0f, (byte)0, "StrikeAbilityScript", 0L, 1f },
                    { 102L, (byte)0, (byte)0, new[] { 3 }, (byte)0, 0L, 0f, 0L, 600L, 0L, 10L, 1, 0L, 0f, "Hunter Shot", false, 0f, 0f, 20, 0f, (byte)0, "StrikeAbilityScript", 0L, 1f },
                    { 103L, (byte)0, (byte)0, new[] { 4 }, (byte)0, 0L, 0f, 300L, 800L, 0L, 5L, 1, 0L, 0f, "Healer Wand", false, 0f, 0f, 10, 0f, (byte)0, "StrikeAbilityScript", 0L, 0.8f }
                });
        }
    }
}
