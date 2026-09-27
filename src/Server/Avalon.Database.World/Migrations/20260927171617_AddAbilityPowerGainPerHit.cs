using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddAbilityPowerGainPerHit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PowerGainPerHit",
                table: "AbilityTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // #526: the column's default already gives every other seeded ability 0, so only Cleave
            // (200) is written; EF emitted a no-op update per row, removed by hand.
            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                column: "PowerGainPerHit",
                value: 8);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_PowerGainPerHit_NonNegative",
                table: "AbilityTemplates",
                sql: "\"PowerGainPerHit\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_PowerGainPerHit_NonNegative",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "PowerGainPerHit",
                table: "AbilityTemplates");
        }
    }
}
