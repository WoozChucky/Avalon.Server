using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddResonanceWorkbench : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Service",
                table: "DoodadTemplates",
                type: "smallint",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 1L,
                column: "Service",
                value: null);

            migrationBuilder.UpdateData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 2L,
                column: "Service",
                value: null);

            migrationBuilder.UpdateData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 3L,
                column: "Service",
                value: null);

            migrationBuilder.UpdateData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 4L,
                column: "Service",
                value: null);

            migrationBuilder.UpdateData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 5L,
                column: "Service",
                value: null);

            migrationBuilder.UpdateData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 6L,
                column: "Service",
                value: null);

            migrationBuilder.InsertData(
                table: "DoodadTemplates",
                columns: new[] { "Id", "AreaRadius", "Collision", "CollisionHalfX", "CollisionHalfZ", "CollisionHeight", "Interaction", "InteractionRange", "InteractionTimeMs", "LootTableId", "MaxGold", "MinGold", "ModelKey", "ModelKit", "Name", "ReadyClip", "RespawnSeconds", "RestorePct", "ScriptName", "Service", "SpentLook", "UsableCondition", "UsableObjectiveId", "UsableQuestId", "UseClip", "UseLimit", "UsedClip", "UsedModelKey", "VisibleCondition", "VisibleObjectiveId", "VisibleQuestId" },
                values: new object[] { 7L, 0f, (byte)0, 0f, 0f, 0f, (byte)1, 5f, 0L, null, 0, 0, "Workbench", "props-kit", "Resonance Workbench", null, null, 0L, null, (byte)1, (byte)1, null, null, null, null, (byte)2, null, null, null, null, null });

            migrationBuilder.InsertData(
                table: "MapDoodadSpawns",
                columns: new[] { "Id", "DoodadTemplateId", "MapTemplateId", "OffsetX", "OffsetY", "OffsetZ", "UsableCondition", "UsableObjectiveId", "UsableQuestId", "VisibleCondition", "VisibleObjectiveId", "VisibleQuestId", "Yaw" },
                values: new object[] { 2, 7L, 1, 21f, 0f, -11f, null, null, null, null, null, null, 0f });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DoodadTemplates_Service",
                table: "DoodadTemplates",
                sql: "\"Service\" IS NULL OR \"Service\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DoodadTemplates_Service",
                table: "DoodadTemplates");

            migrationBuilder.DeleteData(
                table: "MapDoodadSpawns",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 7L);

            migrationBuilder.DropColumn(
                name: "Service",
                table: "DoodadTemplates");
        }
    }
}
