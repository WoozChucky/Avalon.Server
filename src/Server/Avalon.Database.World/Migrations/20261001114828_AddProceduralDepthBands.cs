using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddProceduralDepthBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProceduralDepthBands",
                columns: table => new
                {
                    MinDepth = table.Column<int>(type: "integer", nullable: false),
                    MapTemplateId = table.Column<int>(type: "integer", nullable: false),
                    MaxDepth = table.Column<int>(type: "integer", nullable: true),
                    MinLevel = table.Column<int>(type: "integer", nullable: false),
                    MaxLevel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProceduralDepthBands", x => new { x.MapTemplateId, x.MinDepth });
                    table.CheckConstraint("CK_ProceduralDepthBands_Depth", "\"MinDepth\" >= 0 AND (\"MaxDepth\" IS NULL OR \"MaxDepth\" >= \"MinDepth\")");
                    table.CheckConstraint("CK_ProceduralDepthBands_Level", "\"MinLevel\" >= 1 AND \"MaxLevel\" >= \"MinLevel\"");
                    table.ForeignKey(
                        name: "FK_ProceduralDepthBands_ProceduralMapConfigs_MapTemplateId",
                        column: x => x.MapTemplateId,
                        principalTable: "ProceduralMapConfigs",
                        principalColumn: "MapTemplateId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "MapTemplates",
                keyColumn: "Id",
                keyValue: 2,
                column: "MaxLevel",
                value: 15);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProceduralDepthBands");

            migrationBuilder.UpdateData(
                table: "MapTemplates",
                keyColumn: "Id",
                keyValue: 2,
                column: "MaxLevel",
                value: 5);
        }
    }
}
