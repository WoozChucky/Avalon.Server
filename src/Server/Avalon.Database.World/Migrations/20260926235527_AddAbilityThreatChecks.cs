using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddAbilityThreatChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_HealThreatPerHp_NonNegative",
                table: "AbilityTemplates",
                sql: "\"HealThreatPerHp\" >= 0 AND \"HealThreatPerHp\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_ThreatMultiplier_NonNegative",
                table: "AbilityTemplates",
                sql: "\"ThreatMultiplier\" >= 0 AND \"ThreatMultiplier\" < 'Infinity'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_HealThreatPerHp_NonNegative",
                table: "AbilityTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_ThreatMultiplier_NonNegative",
                table: "AbilityTemplates");
        }
    }
}
