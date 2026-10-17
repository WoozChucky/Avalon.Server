using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddDoodadTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DoodadTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelKit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UsedModelKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReadyClip = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UseClip = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UsedClip = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Interaction = table.Column<byte>(type: "smallint", nullable: false),
                    InteractionTimeMs = table.Column<long>(type: "bigint", nullable: false),
                    InteractionRange = table.Column<float>(type: "real", nullable: false),
                    AreaRadius = table.Column<float>(type: "real", nullable: false),
                    UseLimit = table.Column<byte>(type: "smallint", nullable: false),
                    SpentLook = table.Column<byte>(type: "smallint", nullable: false),
                    RespawnSeconds = table.Column<long>(type: "bigint", nullable: true),
                    ScriptName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LootTableId = table.Column<int>(type: "integer", nullable: true),
                    MinGold = table.Column<int>(type: "integer", nullable: false),
                    MaxGold = table.Column<int>(type: "integer", nullable: false),
                    RestorePct = table.Column<long>(type: "bigint", nullable: false),
                    Collision = table.Column<byte>(type: "smallint", nullable: false),
                    CollisionHalfX = table.Column<float>(type: "real", nullable: false),
                    CollisionHalfZ = table.Column<float>(type: "real", nullable: false),
                    CollisionHeight = table.Column<float>(type: "real", nullable: false),
                    VisibleQuestId = table.Column<long>(type: "bigint", nullable: true),
                    VisibleCondition = table.Column<byte>(type: "smallint", nullable: true),
                    VisibleObjectiveId = table.Column<long>(type: "bigint", nullable: true),
                    UsableQuestId = table.Column<long>(type: "bigint", nullable: true),
                    UsableCondition = table.Column<byte>(type: "smallint", nullable: true),
                    UsableObjectiveId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoodadTemplates", x => x.Id);
                    table.CheckConstraint("CK_DoodadTemplates_AreaRadius", "\"AreaRadius\" >= 0 AND \"AreaRadius\" <= 30");
                    table.CheckConstraint("CK_DoodadTemplates_Collision", "(\"Collision\" = 0) OR (\"Collision\" = 1 AND \"CollisionHalfX\" > 0 AND \"CollisionHalfX\" < 'Infinity' AND \"CollisionHalfZ\" > 0 AND \"CollisionHalfZ\" < 'Infinity' AND \"CollisionHeight\" >= 1.05 AND \"CollisionHeight\" < 'Infinity')");
                    table.CheckConstraint("CK_DoodadTemplates_Gold", "\"MinGold\" >= 0 AND \"MinGold\" <= \"MaxGold\"");
                    table.CheckConstraint("CK_DoodadTemplates_Interaction", "\"Interaction\" IN (0, 1)");
                    table.CheckConstraint("CK_DoodadTemplates_Range", "\"InteractionRange\" > 0 AND \"InteractionRange\" <= 10");
                    table.CheckConstraint("CK_DoodadTemplates_RestorePct", "\"RestorePct\" <= 100");
                    table.CheckConstraint("CK_DoodadTemplates_SpentLook", "\"SpentLook\" IN (1, 2)");
                    table.CheckConstraint("CK_DoodadTemplates_UsableGate", "(\"UsableQuestId\" IS NULL) = (\"UsableCondition\" IS NULL)");
                    table.CheckConstraint("CK_DoodadTemplates_UseLimit", "\"UseLimit\" IN (1, 2)");
                    table.CheckConstraint("CK_DoodadTemplates_VisibleGate", "(\"VisibleQuestId\" IS NULL) = (\"VisibleCondition\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_DoodadTemplates_LootTables_LootTableId",
                        column: x => x.LootTableId,
                        principalTable: "LootTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoodadAuraOutcomes",
                columns: table => new
                {
                    DoodadTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    AuraId = table.Column<long>(type: "bigint", nullable: false),
                    Weight = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoodadAuraOutcomes", x => new { x.DoodadTemplateId, x.AuraId });
                    table.CheckConstraint("CK_DoodadAuraOutcomes_Weight", "\"Weight\" >= 1");
                    table.ForeignKey(
                        name: "FK_DoodadAuraOutcomes_AuraTemplates_AuraId",
                        column: x => x.AuraId,
                        principalTable: "AuraTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoodadAuraOutcomes_DoodadTemplates_DoodadTemplateId",
                        column: x => x.DoodadTemplateId,
                        principalTable: "DoodadTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoodadAuraOutcomes_AuraId",
                table: "DoodadAuraOutcomes",
                column: "AuraId");

            migrationBuilder.CreateIndex(
                name: "IX_DoodadTemplates_LootTableId",
                table: "DoodadTemplates",
                column: "LootTableId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoodadAuraOutcomes");

            migrationBuilder.DropTable(
                name: "DoodadTemplates");
        }
    }
}
