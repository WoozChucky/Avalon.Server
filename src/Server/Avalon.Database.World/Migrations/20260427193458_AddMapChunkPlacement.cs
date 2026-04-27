using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddMapChunkPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MapChunkPlacements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MapTemplateId = table.Column<int>(type: "integer", nullable: false),
                    ChunkTemplateId = table.Column<int>(type: "integer", nullable: false),
                    GridX = table.Column<short>(type: "smallint", nullable: false),
                    GridZ = table.Column<short>(type: "smallint", nullable: false),
                    Rotation = table.Column<byte>(type: "smallint", nullable: false),
                    IsEntry = table.Column<bool>(type: "boolean", nullable: false),
                    EntryLocalX = table.Column<float>(type: "real", nullable: false),
                    EntryLocalY = table.Column<float>(type: "real", nullable: false),
                    EntryLocalZ = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapChunkPlacements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MapChunkPlacements_MapTemplateId",
                table: "MapChunkPlacements",
                column: "MapTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MapChunkPlacements_MapTemplateId_GridX_GridZ",
                table: "MapChunkPlacements",
                columns: new[] { "MapTemplateId", "GridX", "GridZ" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapChunkPlacements");
        }
    }
}
