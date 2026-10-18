using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddUseDoodadObjectives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_QuestObjectives_TargetFitsType",
                table: "QuestObjectives");

            migrationBuilder.AddColumn<long>(
                name: "DoodadTemplateId",
                table: "QuestObjectives",
                type: "bigint",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 101L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 201L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 301L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 302L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 303L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 304L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 401L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 501L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 601L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 602L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 701L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 801L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 802L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestObjectives",
                keyColumn: "Id",
                keyValue: 803L,
                column: "DoodadTemplateId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_QuestObjectives_DoodadTemplateId",
                table: "QuestObjectives",
                column: "DoodadTemplateId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_QuestObjectives_TargetFitsType",
                table: "QuestObjectives",
                sql: "(\"Type\" = 1 AND \"CreatureTemplateId\" IS NOT NULL AND \"ItemTemplateId\" IS NULL AND \"DoodadTemplateId\" IS NULL) OR (\"Type\" = 2 AND \"ItemTemplateId\" IS NOT NULL AND \"CreatureTemplateId\" IS NULL AND \"DoodadTemplateId\" IS NULL) OR (\"Type\" = 3 AND \"CreatureTemplateId\" IS NOT NULL AND \"ItemTemplateId\" IS NULL AND \"DoodadTemplateId\" IS NULL) OR (\"Type\" = 4 AND \"CreatureTemplateId\" IS NULL AND \"ItemTemplateId\" IS NULL AND \"DoodadTemplateId\" IS NULL) OR (\"Type\" = 5 AND \"DoodadTemplateId\" IS NOT NULL AND \"CreatureTemplateId\" IS NULL AND \"ItemTemplateId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_QuestObjectives_DoodadTemplates_DoodadTemplateId",
                table: "QuestObjectives",
                column: "DoodadTemplateId",
                principalTable: "DoodadTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuestObjectives_DoodadTemplates_DoodadTemplateId",
                table: "QuestObjectives");

            migrationBuilder.DropIndex(
                name: "IX_QuestObjectives_DoodadTemplateId",
                table: "QuestObjectives");

            migrationBuilder.DropCheckConstraint(
                name: "CK_QuestObjectives_TargetFitsType",
                table: "QuestObjectives");

            migrationBuilder.DropColumn(
                name: "DoodadTemplateId",
                table: "QuestObjectives");

            migrationBuilder.AddCheckConstraint(
                name: "CK_QuestObjectives_TargetFitsType",
                table: "QuestObjectives",
                sql: "(\"Type\" = 1 AND \"CreatureTemplateId\" IS NOT NULL AND \"ItemTemplateId\" IS NULL) OR (\"Type\" = 2 AND \"ItemTemplateId\" IS NOT NULL AND \"CreatureTemplateId\" IS NULL) OR (\"Type\" = 3 AND \"CreatureTemplateId\" IS NOT NULL AND \"ItemTemplateId\" IS NULL) OR (\"Type\" = 4 AND \"CreatureTemplateId\" IS NULL AND \"ItemTemplateId\" IS NULL)");
        }
    }
}
