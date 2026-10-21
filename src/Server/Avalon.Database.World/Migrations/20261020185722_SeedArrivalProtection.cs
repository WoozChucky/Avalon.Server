using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedArrivalProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AuraTemplates",
                columns: new[] { "Id", "BaseDamageCoefficient", "DurationMs", "Icon", "Kind", "MaxStacks", "Name", "PeriodicBase", "PeriodicKind", "ScalingCoefficient", "ScalingStat", "ScriptName", "Stacking", "TickIntervalMs" },
                values: new object[] { 11L, 0f, 60000L, "arrival protection", (byte)1, 1L, "Arrival Protection", 0f, (byte)0, 0f, (byte)0, null, (byte)1, 0L });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 11L);
        }
    }
}
