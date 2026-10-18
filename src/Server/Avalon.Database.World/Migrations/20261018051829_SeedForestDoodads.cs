using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedForestDoodads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // In foreign-key order: the auras and loot tables before the doodads that name them, and the doodads before
            // their outcomes.
            migrationBuilder.InsertData(
                table: "AuraTemplates",
                columns: new[] { "Id", "BaseDamageCoefficient", "DurationMs", "Icon", "Kind", "MaxStacks", "Name", "PeriodicBase", "PeriodicKind", "ScalingCoefficient", "ScalingStat", "ScriptName", "Stacking", "TickIntervalMs" },
                values: new object[,]
                {
                    { 8L, 0f, 120000L, "blessed", (byte)1, 1L, "Blessed", 0f, (byte)0, 0f, (byte)0, null, (byte)1, 0L },
                    { 9L, 0f, 120000L, "emboldened", (byte)1, 1L, "Emboldened", 0f, (byte)0, 0f, (byte)0, null, (byte)1, 0L },
                    { 10L, 0f, 60000L, "hexed", (byte)2, 1L, "Hexed", 0f, (byte)0, 0f, (byte)0, null, (byte)1, 0L }
                });

            migrationBuilder.InsertData(
                table: "AuraStatModifiers",
                columns: new[] { "AuraId", "Stat", "Kind", "Value" },
                values: new object[,]
                {
                    { 8L, (byte)9, (byte)2, 10f },
                    { 9L, (byte)2, (byte)2, 10f },
                    { 9L, (byte)3, (byte)2, 10f },
                    { 10L, (byte)1, (byte)2, -10f },
                    { 10L, (byte)8, (byte)1, -15f }
                });

            migrationBuilder.InsertData(
                table: "LootTables",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 12, "Forest chest" },
                    { 13, "Forest barrel" }
                });

            migrationBuilder.InsertData(
                table: "LootTableEntries",
                columns: new[] { "LootTableId", "Sequence", "Chance", "GroupId", "ItemTemplateId", "MaxCount", "MinCount", "ReferenceTableId" },
                values: new object[,]
                {
                    { 12, 1, 60f, null, 1m, 2, 1, null },
                    { 12, 2, 40f, null, 2m, 1, 1, null },
                    { 12, 3, 100f, null, null, 1, 1, 1 },
                    { 12, 4, 10f, null, null, 1, 1, 9 },
                    { 12, 5, 25f, null, null, 1, 1, 10 },
                    { 12, 6, 100f, null, null, 1, 1, 11 },
                    { 13, 1, 25f, null, 1m, 1, 1, null },
                    { 13, 2, 15f, null, 2m, 1, 1, null }
                });

            migrationBuilder.InsertData(
                table: "DoodadTemplates",
                columns: new[] { "Id", "AreaRadius", "Collision", "CollisionHalfX", "CollisionHalfZ", "CollisionHeight", "Interaction", "InteractionRange", "InteractionTimeMs", "LootTableId", "MaxGold", "MinGold", "ModelKey", "ModelKit", "Name", "ReadyClip", "RespawnSeconds", "RestorePct", "ScriptName", "SpentLook", "UsableCondition", "UsableObjectiveId", "UsableQuestId", "UseClip", "UseLimit", "UsedClip", "UsedModelKey", "VisibleCondition", "VisibleObjectiveId", "VisibleQuestId" },
                values: new object[,]
                {
                    { 1L, 0f, (byte)0, 0f, 0f, 0f, (byte)1, 5f, 0L, 12, 150, 50, "Chest_Wood", "props-kit", "Forest Chest", "Chest_Closed", null, 0L, "LootDoodadScript", (byte)1, null, null, null, "Chest_Open", (byte)1, "Chest_Opened", null, null, null, null },
                    { 2L, 0f, (byte)0, 0f, 0f, 0f, (byte)1, 5f, 0L, 13, 20, 0, "Barrel", "props-kit", "Old Barrel", null, null, 0L, "LootDoodadScript", (byte)1, null, null, null, null, (byte)1, null, "avalon_barrel_broken", null, null, null },
                    { 3L, 10f, (byte)1, 0.8f, 0.8f, 2.2f, (byte)1, 5f, 0L, null, 0, 0, "avalon_shrine", "town-kit", "Wayside Shrine", null, null, 0L, "ShrineDoodadScript", (byte)1, null, null, null, null, (byte)1, null, "avalon_shrine_spent", null, null, null },
                    { 4L, 10f, (byte)1, 1.05f, 1.05f, 1.8f, (byte)1, 5f, 2000L, null, 0, 0, "avalon_forest_fountain", "town-kit", "Mossy Fountain", null, null, 50L, "FountainDoodadScript", (byte)1, null, null, null, null, (byte)1, null, "avalon_forest_fountain_dry", null, null, null }
                });

            migrationBuilder.InsertData(
                table: "DoodadAuraOutcomes",
                columns: new[] { "AuraId", "DoodadTemplateId", "Weight" },
                values: new object[,]
                {
                    { 8L, 3L, 40L },
                    { 9L, 3L, 40L },
                    { 10L, 3L, 20L }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "DoodadAuraOutcomes",
                keyColumns: new[] { "AuraId", "DoodadTemplateId" },
                keyValues: new object[] { 8L, 3L });

            migrationBuilder.DeleteData(
                table: "DoodadAuraOutcomes",
                keyColumns: new[] { "AuraId", "DoodadTemplateId" },
                keyValues: new object[] { 9L, 3L });

            migrationBuilder.DeleteData(
                table: "DoodadAuraOutcomes",
                keyColumns: new[] { "AuraId", "DoodadTemplateId" },
                keyValues: new object[] { 10L, 3L });

            migrationBuilder.DeleteData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "DoodadTemplates",
                keyColumn: "Id",
                keyValue: 4L);

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 12, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 12, 2 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 12, 3 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 12, 4 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 12, 5 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 12, 6 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 13, 1 });

            migrationBuilder.DeleteData(
                table: "LootTableEntries",
                keyColumns: new[] { "LootTableId", "Sequence" },
                keyValues: new object[] { 13, 2 });

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "LootTables",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 8L, (byte)9 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 9L, (byte)2 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 9L, (byte)3 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 10L, (byte)1 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 10L, (byte)8 });

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 8L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 9L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 10L);
        }
    }
}
