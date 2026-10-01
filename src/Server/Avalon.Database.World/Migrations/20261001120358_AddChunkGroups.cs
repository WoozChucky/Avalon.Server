using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChunkGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ChunkPoolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChunkGroups_ChunkPools_ChunkPoolId",
                        column: x => x.ChunkPoolId,
                        principalTable: "ChunkPools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChunkGroupMembers",
                columns: table => new
                {
                    ChunkGroupId = table.Column<int>(type: "integer", nullable: false),
                    CellX = table.Column<byte>(type: "smallint", nullable: false),
                    CellZ = table.Column<byte>(type: "smallint", nullable: false),
                    ChunkTemplateId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkGroupMembers", x => new { x.ChunkGroupId, x.CellX, x.CellZ });
                    table.ForeignKey(
                        name: "FK_ChunkGroupMembers_ChunkGroups_ChunkGroupId",
                        column: x => x.ChunkGroupId,
                        principalTable: "ChunkGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChunkGroupMembers_ChunkTemplates_ChunkTemplateId",
                        column: x => x.ChunkTemplateId,
                        principalTable: "ChunkTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkGroupMembers_ChunkTemplateId",
                table: "ChunkGroupMembers",
                column: "ChunkTemplateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChunkGroups_ChunkPoolId",
                table: "ChunkGroups",
                column: "ChunkPoolId");

            migrationBuilder.CreateIndex(
                name: "IX_ChunkGroups_Name",
                table: "ChunkGroups",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChunkGroupMembers");

            migrationBuilder.DropTable(
                name: "ChunkGroups");
        }
    }
}
