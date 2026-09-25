using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddCreaturePaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PathId",
                table: "MapCreatureSpawns",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CreaturePaths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreaturePaths", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreaturePathPoints",
                columns: table => new
                {
                    PathId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    OffsetX = table.Column<float>(type: "real", nullable: false),
                    OffsetY = table.Column<float>(type: "real", nullable: false),
                    OffsetZ = table.Column<float>(type: "real", nullable: false),
                    WaitMs = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreaturePathPoints", x => new { x.PathId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_CreaturePathPoints_CreaturePaths_PathId",
                        column: x => x.PathId,
                        principalTable: "CreaturePaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 1,
                column: "PathId",
                value: null);

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 2,
                column: "PathId",
                value: null);

            migrationBuilder.UpdateData(
                table: "MapCreatureSpawns",
                keyColumn: "Id",
                keyValue: 3,
                column: "PathId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_MapCreatureSpawns_PathId",
                table: "MapCreatureSpawns",
                column: "PathId");

            migrationBuilder.AddForeignKey(
                name: "FK_MapCreatureSpawns_CreaturePaths_PathId",
                table: "MapCreatureSpawns",
                column: "PathId",
                principalTable: "CreaturePaths",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MapCreatureSpawns_CreaturePaths_PathId",
                table: "MapCreatureSpawns");

            migrationBuilder.DropTable(
                name: "CreaturePathPoints");

            migrationBuilder.DropTable(
                name: "CreaturePaths");

            migrationBuilder.DropIndex(
                name: "IX_MapCreatureSpawns_PathId",
                table: "MapCreatureSpawns");

            migrationBuilder.DropColumn(
                name: "PathId",
                table: "MapCreatureSpawns");
        }
    }
}
