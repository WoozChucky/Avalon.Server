using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class RenameAbilityScriptAndBaseDamage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_WeaponCoefficient_NonNegative",
                table: "AbilityTemplates");

            migrationBuilder.RenameColumn(
                name: "WeaponCoefficient",
                table: "AbilityTemplates",
                newName: "BaseDamageCoefficient");

            migrationBuilder.RenameColumn(
                name: "SpellScript",
                table: "AbilityTemplates",
                newName: "ScriptName");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_BaseDamageCoefficient_NonNegative",
                table: "AbilityTemplates",
                sql: "\"BaseDamageCoefficient\" >= 0 AND \"BaseDamageCoefficient\" < 'Infinity'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_BaseDamageCoefficient_NonNegative",
                table: "AbilityTemplates");

            migrationBuilder.RenameColumn(
                name: "ScriptName",
                table: "AbilityTemplates",
                newName: "SpellScript");

            migrationBuilder.RenameColumn(
                name: "BaseDamageCoefficient",
                table: "AbilityTemplates",
                newName: "WeaponCoefficient");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_WeaponCoefficient_NonNegative",
                table: "AbilityTemplates",
                sql: "\"WeaponCoefficient\" >= 0 AND \"WeaponCoefficient\" < 'Infinity'");
        }
    }
}
