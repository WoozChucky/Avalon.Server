using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SplitAttackAndCastSpeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_HasteCap",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "HasteCap",
                table: "CombatFormula");

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                column: "Cooldown",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                column: "Cooldown",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 3, null, 1L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 2, null, 1L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 1, null, 1L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 3, null, 1L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 1, null, 1L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 3, null, 1L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 2, null, 1L, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 3, null, 1L, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "HasteCap",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                column: "Cooldown",
                value: 800L);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                column: "Cooldown",
                value: 800L);

            migrationBuilder.UpdateData(
                table: "CombatFormula",
                keyColumn: "Id",
                keyValue: 1,
                column: "HasteCap",
                value: 50f);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                columns: new[] { "StatType1", "StatValue1" },
                values: new object[] { 12, 3L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 12, 3, 0L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 12, 2, 3L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 12, 1, 3L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 12, 3, 2L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 12, 1, 3L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 12, 3, 0L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 12, 2, 3L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                columns: new[] { "StatType1", "StatType2", "StatValue1", "StatValue2" },
                values: new object[] { 12, 3, 2L, 1L });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_HasteCap",
                table: "CombatFormula",
                sql: "\"HasteCap\" >= 0 AND \"HasteCap\" < 'Infinity'");
        }
    }
}
