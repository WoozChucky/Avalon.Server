using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddInstancedMapSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "InstanceType",
                table: "MapTemplates",
                newName: "MapType");

            migrationBuilder.AddColumn<float>(
                name: "DefaultSpawnX",
                table: "MapTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "DefaultSpawnY",
                table: "MapTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "DefaultSpawnZ",
                table: "MapTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "ReturnMapId",
                table: "MapTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MapPortals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceMapId = table.Column<int>(type: "integer", nullable: false),
                    TargetMapId = table.Column<int>(type: "integer", nullable: false),
                    X = table.Column<float>(type: "real", nullable: false),
                    Y = table.Column<float>(type: "real", nullable: false),
                    Z = table.Column<float>(type: "real", nullable: false),
                    Radius = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapPortals", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "MapTemplates",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DefaultSpawnX", "DefaultSpawnY", "DefaultSpawnZ", "MaxPlayers", "ReturnMapId" },
                values: new object[] { 25f, 51f, 25f, 30, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapPortals");

            migrationBuilder.DropColumn(
                name: "DefaultSpawnX",
                table: "MapTemplates");

            migrationBuilder.DropColumn(
                name: "DefaultSpawnY",
                table: "MapTemplates");

            migrationBuilder.DropColumn(
                name: "DefaultSpawnZ",
                table: "MapTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnMapId",
                table: "MapTemplates");

            migrationBuilder.RenameColumn(
                name: "MapType",
                table: "MapTemplates",
                newName: "InstanceType");

            migrationBuilder.UpdateData(
                table: "MapTemplates",
                keyColumn: "Id",
                keyValue: 1,
                column: "MaxPlayers",
                value: 32);
        }
    }
}
