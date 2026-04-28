using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class ExpandForestPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the new forest chunks to ChunkPoolId=1 (forest_pool). Lookup by Name so
            // the migration is robust to whatever Ids the importer assigned.
            migrationBuilder.Sql(@"
                INSERT INTO ""ChunkPoolMembership"" (""ChunkPoolId"", ""ChunkTemplateId"", ""Weight"")
                SELECT 1, ""Id"", 1.0
                FROM ""ChunkTemplates""
                WHERE ""Name"" IN ('forest_path_03', 'forest_path_04', 'forest_junction_t', 'forest_junction_x', 'forest_clearing_01', 'forest_deadend_01')
                ON CONFLICT DO NOTHING;
            ");

            // Lengthen the main path and enable branching now that we have junction + deadend chunks.
            migrationBuilder.Sql(@"
                UPDATE ""ProceduralMapConfigs""
                SET ""MainPathMin"" = 4,
                    ""MainPathMax"" = 7,
                    ""BranchChance"" = 0.4,
                    ""BranchMaxDepth"" = 2
                WHERE ""MapTemplateId"" = 2;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""ProceduralMapConfigs""
                SET ""MainPathMin"" = 2,
                    ""MainPathMax"" = 3,
                    ""BranchChance"" = 0.0,
                    ""BranchMaxDepth"" = 0
                WHERE ""MapTemplateId"" = 2;
            ");

            migrationBuilder.Sql(@"
                DELETE FROM ""ChunkPoolMembership""
                WHERE ""ChunkPoolId"" = 1
                  AND ""ChunkTemplateId"" IN (
                      SELECT ""Id"" FROM ""ChunkTemplates""
                      WHERE ""Name"" IN ('forest_path_03', 'forest_path_04', 'forest_junction_t', 'forest_junction_x', 'forest_clearing_01', 'forest_deadend_01')
                  );
            ");
        }
    }
}
