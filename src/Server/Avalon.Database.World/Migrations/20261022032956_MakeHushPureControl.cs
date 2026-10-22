using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class MakeHushPureControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 235L,
                columns: new[] { "EffectValue", "Effects", "ScalingCoefficient" },
                values: new object[] { 0L, 8, 0f });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 235L,
                columns: new[] { "EffectValue", "Effects", "ScalingCoefficient" },
                values: new object[] { 6L, 1, 0.1f });
        }
    }
}
