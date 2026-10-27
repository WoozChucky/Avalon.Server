using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AbilitiesFromTheTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartingItems",
                table: "CharacterCreateInfos");

            migrationBuilder.DropColumn(
                name: "StartingSpells",
                table: "CharacterCreateInfos");

            migrationBuilder.DropColumn(
                name: "AllowedClasses",
                table: "AbilityTemplates");

            migrationBuilder.CreateTable(
                name: "CharacterCreateItems",
                columns: table => new
                {
                    Class = table.Column<byte>(type: "smallint", nullable: false),
                    Slot = table.Column<byte>(type: "smallint", nullable: false),
                    TemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterCreateItems", x => new { x.Class, x.Slot });
                    table.CheckConstraint("CK_CharacterCreateItems_Slot", "\"Slot\" >= 0");
                    table.ForeignKey(
                        name: "FK_CharacterCreateItems_CharacterCreateInfos_Class",
                        column: x => x.Class,
                        principalTable: "CharacterCreateInfos",
                        principalColumn: "Class",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CharacterCreateItems_ItemTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                columns: new[] { "Cost", "Flags" },
                values: new object[] { 6L, 0L });

            migrationBuilder.InsertData(
                table: "CharacterCreateItems",
                columns: new[] { "Class", "Slot", "TemplateId" },
                values: new object[,]
                {
                    { (byte)1, (byte)0, 1m },
                    { (byte)1, (byte)1, 2m },
                    { (byte)1, (byte)2, 3m },
                    { (byte)2, (byte)0, 1m },
                    { (byte)2, (byte)1, 2m },
                    { (byte)3, (byte)0, 1m },
                    { (byte)3, (byte)1, 2m },
                    { (byte)4, (byte)0, 1m },
                    { (byte)4, (byte)1, 2m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterCreateItems_TemplateId",
                table: "CharacterCreateItems",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterCreateItems");

            migrationBuilder.AddColumn<string>(
                name: "StartingItems",
                table: "CharacterCreateInfos",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StartingSpells",
                table: "CharacterCreateInfos",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "AllowedClasses",
                table: "AbilityTemplates",
                type: "smallint[]",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                columns: new[] { "AllowedClasses", "Cost", "Flags" },
                values: new object[] { new byte[] { 1 }, 0L, 4L });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 201L,
                column: "AllowedClasses",
                value: new byte[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 202L,
                column: "AllowedClasses",
                value: new byte[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 203L,
                column: "AllowedClasses",
                value: new byte[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 204L,
                column: "AllowedClasses",
                value: new byte[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                column: "AllowedClasses",
                value: new byte[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 211L,
                column: "AllowedClasses",
                value: new byte[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 212L,
                column: "AllowedClasses",
                value: new byte[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 213L,
                column: "AllowedClasses",
                value: new byte[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 214L,
                column: "AllowedClasses",
                value: new byte[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                column: "AllowedClasses",
                value: new byte[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 221L,
                column: "AllowedClasses",
                value: new byte[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 222L,
                column: "AllowedClasses",
                value: new byte[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 223L,
                column: "AllowedClasses",
                value: new byte[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 224L,
                column: "AllowedClasses",
                value: new byte[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                column: "AllowedClasses",
                value: new byte[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 231L,
                column: "AllowedClasses",
                value: new byte[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 232L,
                column: "AllowedClasses",
                value: new byte[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 233L,
                column: "AllowedClasses",
                value: new byte[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 234L,
                column: "AllowedClasses",
                value: new byte[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 235L,
                column: "AllowedClasses",
                value: new byte[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 300L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 301L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 302L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 303L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 304L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 305L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 306L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 307L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 308L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 309L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 310L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 311L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 312L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 313L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 314L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 315L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 316L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 317L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 318L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 319L,
                column: "AllowedClasses",
                value: new byte[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 320L,
                column: "AllowedClasses",
                value: new byte[] { 2 });

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)1,
                columns: new[] { "StartingItems", "StartingSpells" },
                values: new object[] { "1,2,3", "200,201,202,203,204" });

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)2,
                columns: new[] { "StartingItems", "StartingSpells" },
                values: new object[] { "1,2", "210,211,212,213,214" });

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)3,
                columns: new[] { "StartingItems", "StartingSpells" },
                values: new object[] { "1,2", "220,221,222,223,224" });

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)4,
                columns: new[] { "StartingItems", "StartingSpells" },
                values: new object[] { "1,2", "230,231,232,233,234,235" });
        }
    }
}
