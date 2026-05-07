using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddAbilityMetadataColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AnimationId",
                table: "AbilityTemplates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Flags",
                table: "AbilityTemplates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<float>(
                name: "HealThreatPerHp",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<long>(
                name: "TauntDurationMs",
                table: "AbilityTemplates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<float>(
                name: "ThreatMultiplier",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 1f);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 1L,
                columns: new[] { "AnimationId", "Flags", "HealThreatPerHp", "TauntDurationMs", "ThreatMultiplier" },
                values: new object[] { 0L, 0L, 0f, 0L, 1f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 2L,
                columns: new[] { "AnimationId", "Flags", "HealThreatPerHp", "TauntDurationMs", "ThreatMultiplier" },
                values: new object[] { 0L, 0L, 0f, 0L, 1f });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnimationId",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "Flags",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "HealThreatPerHp",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "TauntDurationMs",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "ThreatMultiplier",
                table: "AbilityTemplates");
        }
    }
}
