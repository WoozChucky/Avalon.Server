using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SwitchCreatureScriptsToAggroDefend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // V1 seeded Uriel with UrielTownPatrolScript and Borin with UrielPathfinderScript.
            // Both classes were tied to pre-overhaul town world coordinates; deleted in this
            // commit and replaced with the data-driven AggroDefendScript.
            migrationBuilder.Sql(@"
                UPDATE ""CreatureTemplates""
                SET ""ScriptName"" = 'AggroDefendScript'
                WHERE ""ScriptName"" IN ('UrielTownPatrolScript', 'UrielPathfinderScript');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort revert: the original mapping (Id 1 = UrielTownPatrolScript,
            // Id 2 = UrielPathfinderScript) is V1-specific. If the original scripts are
            // gone from the assembly the revert is symbolic — no AI will load.
            migrationBuilder.Sql(@"
                UPDATE ""CreatureTemplates"" SET ""ScriptName"" = 'UrielTownPatrolScript'  WHERE ""Id"" = 1;
                UPDATE ""CreatureTemplates"" SET ""ScriptName"" = 'UrielPathfinderScript'  WHERE ""Id"" = 2;
            ");
        }
    }
}
