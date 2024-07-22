using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.World.Database.Migrations
{
    /// <inheritdoc />
    public partial class V3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (ushort)1,
                column: "StartingSpells",
                value: "1,2");

            migrationBuilder.UpdateData(
                table: "SpellTemplates",
                keyColumn: "Id",
                keyValue: 1u,
                column: "SpellScript",
                value: "StrikeSpellScript");

            migrationBuilder.UpdateData(
                table: "SpellTemplates",
                keyColumn: "Id",
                keyValue: 2u,
                columns: new[] { "AllowedClasses", "CastTime", "Name", "SpellScript" },
                values: new object[] { "[1,2]", 2000u, "Fireball", "FireballSpellScript" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (ushort)1,
                column: "StartingSpells",
                value: "1");

            migrationBuilder.UpdateData(
                table: "SpellTemplates",
                keyColumn: "Id",
                keyValue: 1u,
                column: "SpellScript",
                value: null);

            migrationBuilder.UpdateData(
                table: "SpellTemplates",
                keyColumn: "Id",
                keyValue: 2u,
                columns: new[] { "AllowedClasses", "CastTime", "Name", "SpellScript" },
                values: new object[] { "[2]", 1000u, "Lightning Bolt", null });
        }
    }
}
