using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class RenameSpellTemplatesToAbilityTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve seed data by renaming the underlying table and primary key rather than
            // dropping and recreating. The CLR property `SpellScript` is intentionally left
            // unchanged for now (column name stays `SpellScript`).
            migrationBuilder.DropPrimaryKey(
                name: "PK_SpellTemplates",
                table: "SpellTemplates");

            migrationBuilder.RenameTable(
                name: "SpellTemplates",
                newName: "AbilityTemplates");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AbilityTemplates",
                table: "AbilityTemplates",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AbilityTemplates",
                table: "AbilityTemplates");

            migrationBuilder.RenameTable(
                name: "AbilityTemplates",
                newName: "SpellTemplates");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SpellTemplates",
                table: "SpellTemplates",
                column: "Id");
        }
    }
}
