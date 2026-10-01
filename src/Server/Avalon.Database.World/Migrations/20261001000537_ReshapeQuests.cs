using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeQuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestRewards");

            migrationBuilder.DropTable(
                name: "QuestRewardTemplates");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "QuestTemplates");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "QuestTemplates");

            migrationBuilder.RenameColumn(
                name: "CompletionCriteriaId",
                table: "QuestTemplates",
                newName: "TitleTextId");

            migrationBuilder.AlterColumn<long>(
                name: "RequiredQuestId",
                table: "QuestTemplates",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "GiverCreatureId",
                table: "QuestTemplates",
                type: "numeric(20,0)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "EnderCreatureId",
                table: "QuestTemplates",
                type: "numeric(20,0)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "ClassRequirement",
                table: "QuestTemplates",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "QuestTemplates",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "CompletionTextId",
                table: "QuestTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DescriptionTextId",
                table: "QuestTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "RewardExperience",
                table: "QuestTemplates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "RewardMoney",
                table: "QuestTemplates",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ScriptName",
                table: "QuestTemplates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuestItemRewards",
                columns: table => new
                {
                    QuestId = table.Column<long>(type: "bigint", nullable: false),
                    ItemTemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestItemRewards", x => new { x.QuestId, x.ItemTemplateId });
                    table.CheckConstraint("CK_QuestItemRewards_CountPositive", "\"Count\" >= 1");
                    table.ForeignKey(
                        name: "FK_QuestItemRewards_ItemTemplates_ItemTemplateId",
                        column: x => x.ItemTemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuestItemRewards_QuestTemplates_QuestId",
                        column: x => x.QuestId,
                        principalTable: "QuestTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestStages",
                columns: table => new
                {
                    QuestId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    DescriptionTextId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestStages", x => new { x.QuestId, x.Sequence });
                    table.CheckConstraint("CK_QuestStages_Sequence", "\"Sequence\" >= 0");
                    table.ForeignKey(
                        name: "FK_QuestStages_QuestTemplates_QuestId",
                        column: x => x.QuestId,
                        principalTable: "QuestTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestObjectives",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    QuestId = table.Column<long>(type: "bigint", nullable: false),
                    StageSequence = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    CreatureTemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    ItemTemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    DescriptionTextId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestObjectives", x => x.Id);
                    table.CheckConstraint("CK_QuestObjectives_CountPositive", "\"Count\" >= 1");
                    table.CheckConstraint("CK_QuestObjectives_TargetFitsType", "(\"Type\" = 1 AND \"CreatureTemplateId\" IS NOT NULL AND \"ItemTemplateId\" IS NULL) OR (\"Type\" = 2 AND \"ItemTemplateId\" IS NOT NULL AND \"CreatureTemplateId\" IS NULL) OR (\"Type\" = 3 AND \"CreatureTemplateId\" IS NOT NULL AND \"ItemTemplateId\" IS NULL) OR (\"Type\" = 4 AND \"CreatureTemplateId\" IS NULL AND \"ItemTemplateId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_QuestObjectives_CreatureTemplates_CreatureTemplateId",
                        column: x => x.CreatureTemplateId,
                        principalTable: "CreatureTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuestObjectives_ItemTemplates_ItemTemplateId",
                        column: x => x.ItemTemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuestObjectives_QuestStages_QuestId_StageSequence",
                        columns: x => new { x.QuestId, x.StageSequence },
                        principalTable: "QuestStages",
                        principalColumns: new[] { "QuestId", "Sequence" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestObjectives_QuestTemplates_QuestId",
                        column: x => x.QuestId,
                        principalTable: "QuestTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestItemDrops",
                columns: table => new
                {
                    ObjectiveId = table.Column<long>(type: "bigint", nullable: false),
                    CreatureTemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Chance = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestItemDrops", x => new { x.ObjectiveId, x.CreatureTemplateId });
                    table.CheckConstraint("CK_QuestItemDrops_Chance", "\"Chance\" >= 0 AND \"Chance\" <= 100");
                    table.ForeignKey(
                        name: "FK_QuestItemDrops_CreatureTemplates_CreatureTemplateId",
                        column: x => x.CreatureTemplateId,
                        principalTable: "CreatureTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuestItemDrops_QuestObjectives_ObjectiveId",
                        column: x => x.ObjectiveId,
                        principalTable: "QuestObjectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestTemplates_EnderCreatureId",
                table: "QuestTemplates",
                column: "EnderCreatureId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestTemplates_GiverCreatureId",
                table: "QuestTemplates",
                column: "GiverCreatureId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestTemplates_RequiredQuestId",
                table: "QuestTemplates",
                column: "RequiredQuestId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestItemDrops_CreatureTemplateId",
                table: "QuestItemDrops",
                column: "CreatureTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestItemRewards_ItemTemplateId",
                table: "QuestItemRewards",
                column: "ItemTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestObjectives_CreatureTemplateId",
                table: "QuestObjectives",
                column: "CreatureTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestObjectives_ItemTemplateId",
                table: "QuestObjectives",
                column: "ItemTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestObjectives_QuestId_StageSequence",
                table: "QuestObjectives",
                columns: new[] { "QuestId", "StageSequence" });

            migrationBuilder.AddForeignKey(
                name: "FK_QuestTemplates_CreatureTemplates_EnderCreatureId",
                table: "QuestTemplates",
                column: "EnderCreatureId",
                principalTable: "CreatureTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QuestTemplates_CreatureTemplates_GiverCreatureId",
                table: "QuestTemplates",
                column: "GiverCreatureId",
                principalTable: "CreatureTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QuestTemplates_QuestTemplates_RequiredQuestId",
                table: "QuestTemplates",
                column: "RequiredQuestId",
                principalTable: "QuestTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuestTemplates_CreatureTemplates_EnderCreatureId",
                table: "QuestTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_QuestTemplates_CreatureTemplates_GiverCreatureId",
                table: "QuestTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_QuestTemplates_QuestTemplates_RequiredQuestId",
                table: "QuestTemplates");

            migrationBuilder.DropTable(
                name: "QuestItemDrops");

            migrationBuilder.DropTable(
                name: "QuestItemRewards");

            migrationBuilder.DropTable(
                name: "QuestObjectives");

            migrationBuilder.DropTable(
                name: "QuestStages");

            migrationBuilder.DropIndex(
                name: "IX_QuestTemplates_EnderCreatureId",
                table: "QuestTemplates");

            migrationBuilder.DropIndex(
                name: "IX_QuestTemplates_GiverCreatureId",
                table: "QuestTemplates");

            migrationBuilder.DropIndex(
                name: "IX_QuestTemplates_RequiredQuestId",
                table: "QuestTemplates");

            migrationBuilder.DropColumn(
                name: "CompletionTextId",
                table: "QuestTemplates");

            migrationBuilder.DropColumn(
                name: "DescriptionTextId",
                table: "QuestTemplates");

            migrationBuilder.DropColumn(
                name: "RewardExperience",
                table: "QuestTemplates");

            migrationBuilder.DropColumn(
                name: "RewardMoney",
                table: "QuestTemplates");

            migrationBuilder.DropColumn(
                name: "ScriptName",
                table: "QuestTemplates");

            migrationBuilder.RenameColumn(
                name: "TitleTextId",
                table: "QuestTemplates",
                newName: "CompletionCriteriaId");

            migrationBuilder.AlterColumn<int>(
                name: "RequiredQuestId",
                table: "QuestTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "GiverCreatureId",
                table: "QuestTemplates",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,0)");

            migrationBuilder.AlterColumn<int>(
                name: "EnderCreatureId",
                table: "QuestTemplates",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,0)");

            migrationBuilder.AlterColumn<int>(
                name: "ClassRequirement",
                table: "QuestTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "QuestTemplates",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "QuestTemplates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "QuestTemplates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "QuestRewardTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestRewardTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuestRewards",
                columns: table => new
                {
                    QuestId = table.Column<long>(type: "bigint", nullable: false),
                    RewardId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestRewards", x => new { x.QuestId, x.RewardId });
                    table.ForeignKey(
                        name: "FK_QuestRewards_QuestRewardTemplates_RewardId",
                        column: x => x.RewardId,
                        principalTable: "QuestRewardTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestRewards_QuestTemplates_QuestId",
                        column: x => x.QuestId,
                        principalTable: "QuestTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestRewards_RewardId",
                table: "QuestRewards",
                column: "RewardId");
        }
    }
}
