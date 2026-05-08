using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAbilityScriptNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 1L,
                column: "SpellScript",
                value: "StrikeAbilityScript");

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 2L,
                column: "SpellScript",
                value: "FireballAbilityScript");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 1L,
                column: "SpellScript",
                value: "StrikeSpellScript");

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 2L,
                column: "SpellScript",
                value: "FireballSpellScript");
        }
    }
}
