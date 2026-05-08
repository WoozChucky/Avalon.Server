using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedBasicAttackAbilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AbilityTemplates",
                columns: new[] { "Id", "AllowedClasses", "AnimationId", "CastTime", "Cooldown", "Cost", "EffectValue", "Effects", "Flags", "HealThreatPerHp", "Name", "Range", "SpellScript", "TauntDurationMs", "ThreatMultiplier" },
                values: new object[,]
                {
                    { 100L, new[] { 1 }, 0L, 0L, 500L, 0L, 15L, 1, 0L, 0f, "Warrior Slash", 1, "StrikeAbilityScript", 0L, 1.5f },
                    { 101L, new[] { 2 }, 0L, 200L, 700L, 0L, 8L, 1, 0L, 0f, "Wizard Bolt", 10, "StrikeAbilityScript", 0L, 1f },
                    { 102L, new[] { 3 }, 0L, 0L, 600L, 0L, 10L, 1, 0L, 0f, "Hunter Shot", 20, "StrikeAbilityScript", 0L, 1f },
                    { 103L, new[] { 4 }, 0L, 300L, 800L, 0L, 5L, 1, 0L, 0f, "Healer Wand", 10, "StrikeAbilityScript", 0L, 0.8f }
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 1,
                column: "StartingSpells",
                value: "1,2");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 2,
                column: "StartingSpells",
                value: "2");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 3,
                column: "StartingSpells",
                value: "");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 4,
                column: "StartingSpells",
                value: "");
        }
    }
}
