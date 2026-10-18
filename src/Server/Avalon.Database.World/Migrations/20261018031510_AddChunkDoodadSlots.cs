using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkDoodadSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChunkDoodadSlot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Tag = table.Column<string>(type: "text", nullable: false),
                    LocalX = table.Column<float>(type: "real", nullable: false),
                    LocalY = table.Column<float>(type: "real", nullable: false),
                    LocalZ = table.Column<float>(type: "real", nullable: false),
                    Yaw = table.Column<int>(type: "integer", nullable: false),
                    ChunkTemplateId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkDoodadSlot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChunkDoodadSlot_ChunkTemplates_ChunkTemplateId",
                        column: x => x.ChunkTemplateId,
                        principalTable: "ChunkTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChunkDoodadSlot_ChunkTemplateId",
                table: "ChunkDoodadSlot",
                column: "ChunkTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChunkDoodadSlot");
        }
    }
}
