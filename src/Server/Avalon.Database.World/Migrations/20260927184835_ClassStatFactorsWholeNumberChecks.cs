using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class ClassStatFactorsWholeNumberChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassStatFactors_FixedPower",
                table: "ClassStatFactors",
                sql: "\"FixedPower\" IS NULL OR (\"FixedPower\" >= 0 AND \"FixedPower\" <= 4294967295)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassStatFactors_HpPerStamina",
                table: "ClassStatFactors",
                sql: "\"HpPerStamina\" >= 0 AND \"HpPerStamina\" <= 4294967295");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassStatFactors_FixedPower",
                table: "ClassStatFactors");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassStatFactors_HpPerStamina",
                table: "ClassStatFactors");
        }
    }
}
