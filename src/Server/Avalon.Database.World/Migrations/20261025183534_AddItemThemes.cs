using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddItemThemes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reordered by hand (#1180): the themes exist before any group names one, and the 45 affix groups carry
            // their themes before the check and the foreign key hold them to it. EF also emitted a no-op update per
            // unique and implicit group (ThemeId null on a column added null), removed by hand.
            migrationBuilder.CreateTable(
                name: "ItemThemes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemThemes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemThemes_Name",
                table: "ItemThemes",
                column: "Name",
                unique: true);

            migrationBuilder.InsertData(
                table: "ItemThemes",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Ember" },
                    { 2, "Rime" },
                    { 3, "Storm" },
                    { 4, "Wild" },
                    { 5, "Iron" },
                    { 6, "Thorn" },
                    { 7, "Gale" },
                    { 8, "Veil" }
                });

            migrationBuilder.AddColumn<int>(
                name: "ThemeId",
                table: "ItemModifierGroups",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 1,
                column: "ThemeId",
                value: 6);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 2,
                column: "ThemeId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 3,
                column: "ThemeId",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 4,
                column: "ThemeId",
                value: 3);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 5,
                column: "ThemeId",
                value: 4);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 6,
                column: "ThemeId",
                value: 6);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 7,
                column: "ThemeId",
                value: 8);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 8,
                column: "ThemeId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 9,
                column: "ThemeId",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 10,
                column: "ThemeId",
                value: 3);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 11,
                column: "ThemeId",
                value: 4);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 12,
                column: "ThemeId",
                value: 6);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 13,
                column: "ThemeId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 14,
                column: "ThemeId",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 15,
                column: "ThemeId",
                value: 3);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 16,
                column: "ThemeId",
                value: 4);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 17,
                column: "ThemeId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 18,
                column: "ThemeId",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 19,
                column: "ThemeId",
                value: 3);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 20,
                column: "ThemeId",
                value: 4);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 21,
                column: "ThemeId",
                value: 5);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 22,
                column: "ThemeId",
                value: 5);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 23,
                column: "ThemeId",
                value: 7);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 24,
                column: "ThemeId",
                value: 7);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 25,
                column: "ThemeId",
                value: 8);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 26,
                column: "ThemeId",
                value: 8);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 27,
                column: "ThemeId",
                value: 4);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 28,
                column: "ThemeId",
                value: 4);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 29,
                column: "ThemeId",
                value: 6);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 30,
                column: "ThemeId",
                value: 7);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 31,
                column: "ThemeId",
                value: 8);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 32,
                column: "ThemeId",
                value: 4);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 33,
                column: "ThemeId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 34,
                column: "ThemeId",
                value: 2);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 35,
                column: "ThemeId",
                value: 3);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 36,
                column: "ThemeId",
                value: 4);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 37,
                column: "ThemeId",
                value: 7);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 38,
                column: "ThemeId",
                value: 7);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 39,
                column: "ThemeId",
                value: 8);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 40,
                column: "ThemeId",
                value: 6);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 41,
                column: "ThemeId",
                value: 6);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 42,
                column: "ThemeId",
                value: 7);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 43,
                column: "ThemeId",
                value: 8);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 44,
                column: "ThemeId",
                value: 5);

            migrationBuilder.UpdateData(
                table: "ItemModifierGroups",
                keyColumn: "Id",
                keyValue: 110,
                column: "ThemeId",
                value: 4);

            migrationBuilder.CreateIndex(
                name: "IX_ItemModifierGroups_ThemeId",
                table: "ItemModifierGroups",
                column: "ThemeId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemModifierGroups_Theme",
                table: "ItemModifierGroups",
                sql: "(\"Kind\" IN (1, 2)) = (\"ThemeId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ItemModifierGroups_ItemThemes_ThemeId",
                table: "ItemModifierGroups",
                column: "ThemeId",
                principalTable: "ItemThemes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ItemModifierGroups_ItemThemes_ThemeId",
                table: "ItemModifierGroups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemModifierGroups_Theme",
                table: "ItemModifierGroups");

            migrationBuilder.DropIndex(
                name: "IX_ItemModifierGroups_ThemeId",
                table: "ItemModifierGroups");

            migrationBuilder.DropColumn(
                name: "ThemeId",
                table: "ItemModifierGroups");

            migrationBuilder.DropTable(
                name: "ItemThemes");
        }
    }
}
