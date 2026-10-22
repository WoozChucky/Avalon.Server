using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class ItemsAllowEveryClassWhenEmpty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 56m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 57m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 58m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 59m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 60m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 61m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 62m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 63m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 64m,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 65m,
                column: "AllowedClasses",
                value: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 56m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 57m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 58m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 59m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 60m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 61m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 62m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 63m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 64m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 65m,
                column: "AllowedClasses",
                value: new byte[] { 1, 2, 3, 4 });
        }
    }
}
