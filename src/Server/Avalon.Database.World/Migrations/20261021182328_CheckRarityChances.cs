using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class CheckRarityChances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureRarityModifiers_BlockPct",
                table: "CreatureRarityModifiers",
                sql: "\"BlockPct\" >= 0 AND \"BlockPct\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureRarityModifiers_CritPct",
                table: "CreatureRarityModifiers",
                sql: "\"CritPct\" >= 0 AND \"CritPct\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureRarityModifiers_DodgePct",
                table: "CreatureRarityModifiers",
                sql: "\"DodgePct\" >= 0 AND \"DodgePct\" <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureRarityModifiers_BlockPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureRarityModifiers_CritPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureRarityModifiers_DodgePct",
                table: "CreatureRarityModifiers");
        }
    }
}
