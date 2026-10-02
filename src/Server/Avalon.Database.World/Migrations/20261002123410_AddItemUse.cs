using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddItemUse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UseCastTimeMs",
                table: "ItemTemplates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UseCooldownGroup",
                table: "ItemTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UseCooldownMs",
                table: "ItemTemplates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UseScript",
                table: "ItemTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UseValue",
                table: "ItemTemplates",
                type: "bigint",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, "potion", 30000L, "RestoreHealth", 30L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, "potion", 30000L, "RestorePower", 30L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { 3000L, null, 30000L, "TownPortalScroll", null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 15m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 16m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 17m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 18m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 19m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 20m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 21m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 22m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 23m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 24m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 25m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 26m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 27m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 28m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 29m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 30m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 31m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 36m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 37m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 38m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 39m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 40m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 41m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 42m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 43m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 44m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 45m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 46m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 47m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 48m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 49m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 50m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 51m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 52m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 53m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 54m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 55m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 56m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, "potion", 30000L, "RestoreHealth", 60L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 57m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 58m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 59m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 60m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 61m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 62m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 63m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 64m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 65m,
                columns: new[] { "UseCastTimeMs", "UseCooldownGroup", "UseCooldownMs", "UseScript", "UseValue" },
                values: new object[] { null, null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseCastTimeMs",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "UseCooldownGroup",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "UseCooldownMs",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "UseScript",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "UseValue",
                table: "ItemTemplates");
        }
    }
}
