using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedForestProcedural : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ChunkPools",
                columns: new[] { "Id", "Name" },
                values: new object[] { 1, "forest_pool" });

            migrationBuilder.InsertData(
                table: "SpawnTables",
                columns: new[] { "Id", "Name" },
                values: new object[] { 1, "forest_creatures" });

            // Placeholder creature mix using the only seeded CreatureTemplates (Uriel=1, Borin=2, Innkeeper=3).
            // Real forest creatures should replace these once authored.
            migrationBuilder.InsertData(
                table: "SpawnTableEntry",
                columns: new[] { "Id", "SpawnTableId", "Tag", "CreatureId", "Weight", "MinCount", "MaxCount" },
                values: new object[,]
                {
                    { 1, 1, "pack", 2m,  1.0f, (byte)1, (byte)2 },
                    { 2, 1, "pack", 3m,  0.5f, (byte)1, (byte)1 },
                    { 3, 1, "rare", 3m,  1.0f, (byte)1, (byte)1 },
                    { 4, 1, "boss", 1m,  1.0f, (byte)1, (byte)1 },
                });

            migrationBuilder.InsertData(
                table: "ProceduralMapConfigs",
                columns: new[]
                {
                    "MapTemplateId", "ChunkPoolId", "SpawnTableId",
                    "MainPathMin", "MainPathMax", "BranchChance", "BranchMaxDepth",
                    "HasBoss", "BackPortalTargetMapId", "ForwardPortalTargetMapId"
                },
                values: new object[]
                {
                    2, 1, 1,
                    2, 3, 0.0f, (byte)0,
                    true, 1, null
                });

            // Pool membership references chunk templates that are imported via the
            // Avalon.ChunkImporter CLI, not seeded here. Look up by Name so the migration
            // is robust to whatever Ids the importer assigned. If chunks are not yet
            // imported, this migration is still safe — zero rows inserted, ChunkLibrary
            // validation will fail at world startup until chunks land.
            migrationBuilder.Sql(@"
                INSERT INTO ""ChunkPoolMembership"" (""ChunkPoolId"", ""ChunkTemplateId"", ""Weight"")
                SELECT 1, ""Id"", 1.0
                FROM ""ChunkTemplates""
                WHERE ""Name"" IN ('forest_entry_01', 'forest_path_01', 'forest_path_02', 'forest_boss_01')
                ON CONFLICT DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""ChunkPoolMembership"" WHERE ""ChunkPoolId"" = 1;");

            migrationBuilder.DeleteData(
                table: "ProceduralMapConfigs",
                keyColumn: "MapTemplateId",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "SpawnTableEntry",
                keyColumn: "Id",
                keyValues: new object[] { 1, 2, 3, 4 });

            migrationBuilder.DeleteData(
                table: "SpawnTables",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "ChunkPools",
                keyColumn: "Id",
                keyValue: 1);
        }
    }
}
