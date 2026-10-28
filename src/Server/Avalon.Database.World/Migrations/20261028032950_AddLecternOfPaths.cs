using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddLecternOfPaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reordered by hand: the widened check is in place before row 8 (Service 2) is inserted, and Down deletes
            // rows 3 and 8 before it narrows the check again.
            migrationBuilder.DropCheckConstraint(
                name: "CK_DoodadTemplates_Service",
                table: "DoodadTemplates");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DoodadTemplates_Service",
                table: "DoodadTemplates",
                sql: "\"Service\" IS NULL OR \"Service\" IN (1, 2)");

            migrationBuilder.InsertData(
                table: "DoodadTemplates",
                columns: new[] { "Id", "AreaRadius", "Collision", "CollisionHalfX", "CollisionHalfZ", "CollisionHeight", "Interaction", "InteractionRange", "InteractionTimeMs", "LootTableId", "MaxGold", "MinGold", "ModelKey", "ModelKit", "Name", "ReadyClip", "RespawnSeconds", "RestorePct", "ScriptName", "Service", "SpentLook", "UsableCondition", "UsableObjectiveId", "UsableQuestId", "UseClip", "UseLimit", "UsedClip", "UsedModelKey", "VisibleCondition", "VisibleObjectiveId", "VisibleQuestId" },
                values: new object[] { 8L, 0f, (byte)0, 0f, 0f, 0f, (byte)1, 5f, 0L, null, 0, 0, "BookStand", "props-kit", "Lectern of Paths", null, null, 0L, null, (byte)2, (byte)1, null, null, null, null, (byte)2, null, null, null, null, null });

            migrationBuilder.InsertData(
                table: "MapDoodadSpawns",
                columns: new[] { "Id", "DoodadTemplateId", "MapTemplateId", "OffsetX", "OffsetY", "OffsetZ", "UsableCondition", "UsableObjectiveId", "UsableQuestId", "VisibleCondition", "VisibleObjectiveId", "VisibleQuestId", "Yaw" },
                values: new object[] { 3, 8L, 1, 18f, 0f, -11f, null, null, null, null, null, null, 90f });
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
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 8L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_DoodadTemplates_Service",
                table: "DoodadTemplates",
                sql: "\"Service\" IS NULL OR \"Service\" = 1");
        }
    }
}
