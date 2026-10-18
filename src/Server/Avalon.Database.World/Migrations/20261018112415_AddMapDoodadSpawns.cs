using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddMapDoodadSpawns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MapDoodadSpawns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MapTemplateId = table.Column<int>(type: "integer", nullable: false),
                    DoodadTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    OffsetX = table.Column<float>(type: "real", nullable: false),
                    OffsetY = table.Column<float>(type: "real", nullable: false),
                    OffsetZ = table.Column<float>(type: "real", nullable: false),
                    Yaw = table.Column<float>(type: "real", nullable: false),
                    VisibleQuestId = table.Column<long>(type: "bigint", nullable: true),
                    VisibleCondition = table.Column<byte>(type: "smallint", nullable: true),
                    VisibleObjectiveId = table.Column<long>(type: "bigint", nullable: true),
                    UsableQuestId = table.Column<long>(type: "bigint", nullable: true),
                    UsableCondition = table.Column<byte>(type: "smallint", nullable: true),
                    UsableObjectiveId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapDoodadSpawns", x => x.Id);
                    table.CheckConstraint("CK_MapDoodadSpawns_UsableGate", "(\"UsableQuestId\" IS NULL) = (\"UsableCondition\" IS NULL)");
                    table.CheckConstraint("CK_MapDoodadSpawns_VisibleGate", "(\"VisibleQuestId\" IS NULL) = (\"VisibleCondition\" IS NULL)");
                    table.CheckConstraint("CK_MapDoodadSpawns_Yaw", "\"Yaw\" >= 0 AND \"Yaw\" < 360");
                    table.ForeignKey(
                        name: "FK_MapDoodadSpawns_DoodadTemplates_DoodadTemplateId",
                        column: x => x.DoodadTemplateId,
                        principalTable: "DoodadTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "MapDoodadSpawns",
                columns: new[] { "Id", "DoodadTemplateId", "MapTemplateId", "OffsetX", "OffsetY", "OffsetZ", "UsableCondition", "UsableObjectiveId", "UsableQuestId", "VisibleCondition", "VisibleObjectiveId", "VisibleQuestId", "Yaw" },
                values: new object[] { 1, 6L, 1, -6f, 0f, 9f, null, null, null, null, null, null, 90f });

            migrationBuilder.CreateIndex(
                name: "IX_MapDoodadSpawns_DoodadTemplateId",
                table: "MapDoodadSpawns",
                column: "DoodadTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MapDoodadSpawns_MapTemplateId",
                table: "MapDoodadSpawns",
                column: "MapTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapDoodadSpawns");
        }
    }
}
