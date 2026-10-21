using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class RenameHealerToCleric : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 14 },
                column: "Text",
                value: "Clérig{g:o|a}");

            migrationBuilder.UpdateData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 14,
                column: "Text",
                value: "Cleric");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "LocalizedTextLocales",
                keyColumns: new[] { "Locale", "TextId" },
                keyValues: new object[] { "ptPT", 14 },
                column: "Text",
                value: "Curandeir{g:o|a}");

            migrationBuilder.UpdateData(
                table: "LocalizedTexts",
                keyColumn: "Id",
                keyValue: 14,
                column: "Text",
                value: "Healer");
        }
    }
}
