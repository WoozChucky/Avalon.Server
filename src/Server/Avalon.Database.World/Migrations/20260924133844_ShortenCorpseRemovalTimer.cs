using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class ShortenCorpseRemovalTimer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "BodyRemoveTimerSecs",
                value: 10);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "BodyRemoveTimerSecs",
                value: 10);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "BodyRemoveTimerSecs",
                value: 10);

            // Hand-written because EF only scaffolds the three HasData-owned rows above; any template
            // added outside seeding still holds the old 120 default.
            migrationBuilder.Sql(@"
                UPDATE ""CreatureTemplates""
                SET ""BodyRemoveTimerSecs"" = 10
                WHERE ""BodyRemoveTimerSecs"" = 120;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "BodyRemoveTimerSecs",
                value: 120);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "BodyRemoveTimerSecs",
                value: 120);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "BodyRemoveTimerSecs",
                value: 120);

            // Best-effort, and deliberately narrower than Up: which rows held 120 before Up ran is
            // not recoverable, so this restores the old default only where the new one is still in
            // place and leaves any value chosen since then alone.
            migrationBuilder.Sql(@"
                UPDATE ""CreatureTemplates""
                SET ""BodyRemoveTimerSecs"" = 120
                WHERE ""BodyRemoveTimerSecs"" = 10;
            ");
        }
    }
}
