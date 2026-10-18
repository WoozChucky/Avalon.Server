using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddProceduralDoodadRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProceduralDoodadRules",
                columns: table => new
                {
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    MapTemplateId = table.Column<int>(type: "integer", nullable: false),
                    DoodadTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    SlotTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MinCount = table.Column<byte>(type: "smallint", nullable: false),
                    MaxCount = table.Column<byte>(type: "smallint", nullable: false),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    MinSpacing = table.Column<float>(type: "real", nullable: false),
                    MinDepth = table.Column<int>(type: "integer", nullable: true),
                    MaxDepth = table.Column<int>(type: "integer", nullable: true),
                    VisibleQuestId = table.Column<long>(type: "bigint", nullable: true),
                    VisibleCondition = table.Column<byte>(type: "smallint", nullable: true),
                    VisibleObjectiveId = table.Column<long>(type: "bigint", nullable: true),
                    UsableQuestId = table.Column<long>(type: "bigint", nullable: true),
                    UsableCondition = table.Column<byte>(type: "smallint", nullable: true),
                    UsableObjectiveId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProceduralDoodadRules", x => new { x.MapTemplateId, x.Sequence });
                    table.CheckConstraint("CK_ProceduralDoodadRules_Count", "\"MinCount\" >= 0 AND \"MinCount\" <= \"MaxCount\" AND \"MaxCount\" <= 50");
                    table.CheckConstraint("CK_ProceduralDoodadRules_Spacing", "\"MinSpacing\" >= 0 AND \"MinSpacing\" < 'Infinity'");
                    table.CheckConstraint("CK_ProceduralDoodadRules_UsableGate", "(\"UsableQuestId\" IS NULL) = (\"UsableCondition\" IS NULL)");
                    table.CheckConstraint("CK_ProceduralDoodadRules_VisibleGate", "(\"VisibleQuestId\" IS NULL) = (\"VisibleCondition\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_ProceduralDoodadRules_ProceduralMapConfigs_MapTemplateId",
                        column: x => x.MapTemplateId,
                        principalTable: "ProceduralMapConfigs",
                        principalColumn: "MapTemplateId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProceduralDoodadRules");
        }
    }
}
