using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddTownNpcSpawns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Invulnerable",
                table: "CreatureTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MapCreatureSpawns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MapTemplateId = table.Column<int>(type: "integer", nullable: false),
                    CreatureTemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    OffsetX = table.Column<float>(type: "real", nullable: false),
                    OffsetY = table.Column<float>(type: "real", nullable: false),
                    OffsetZ = table.Column<float>(type: "real", nullable: false),
                    Facing = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapCreatureSpawns", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                columns: new[] { "Exp", "Invulnerable", "ScriptName" },
                values: new object[] { 0L, true, "TownNpcScript" });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                columns: new[] { "Exp", "Invulnerable", "ScriptName" },
                values: new object[] { 0L, true, "TownNpcScript" });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                columns: new[] { "Exp", "Invulnerable", "ScriptName" },
                values: new object[] { 0L, true, "TownNpcScript" });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "Invulnerable",
                value: false);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "Invulnerable",
                value: false);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "Invulnerable",
                value: false);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "Invulnerable",
                value: false);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "Invulnerable",
                value: false);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "Invulnerable",
                value: false);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "Invulnerable",
                value: false);

            migrationBuilder.InsertData(
                table: "MapCreatureSpawns",
                columns: new[] { "Id", "CreatureTemplateId", "Facing", "MapTemplateId", "OffsetX", "OffsetY", "OffsetZ" },
                values: new object[,]
                {
                    { 1, 1m, 143f, 1, -3f, 0f, 4f },
                    { 2, 2m, 217f, 1, 3f, 0f, 4f },
                    { 3, 3m, 180f, 1, 0f, 0f, 7f }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MapCreatureSpawns_MapTemplateId",
                table: "MapCreatureSpawns",
                column: "MapTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapCreatureSpawns");

            migrationBuilder.DropColumn(
                name: "Invulnerable",
                table: "CreatureTemplates");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                columns: new[] { "Exp", "ScriptName" },
                values: new object[] { 20L, "CreatureIdleScript" });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                columns: new[] { "Exp", "ScriptName" },
                values: new object[] { 20L, "CreatureIdleScript" });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                columns: new[] { "Exp", "ScriptName" },
                values: new object[] { 20L, "CreatureIdleScript" });
        }
    }
}
