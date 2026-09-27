using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <summary>
    /// #627: the combat formula's haste cap and movement speed bounds, every creature's BaseAttackTime as a
    /// swing interval in seconds (a real, 2.25 for every seeded template, at least 0.5), and the weapons'
    /// AttackSpeed as a haste percentage. The check constraints are added after the updates they check.
    /// </summary>
    public partial class AddHasteAndMoveSpeedBounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<float>(
                name: "BaseAttackTime",
                table: "CreatureTemplates",
                type: "real",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<float>(
                name: "HasteCap",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "MoveSpeedCap",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "MoveSpeedFloor",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.UpdateData(
                table: "CombatFormula",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "HasteCap", "MoveSpeedCap", "MoveSpeedFloor" },
                values: new object[] { 50f, 50f, -50f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                column: "BaseAttackTime",
                value: 2.25f);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "StatValue1",
                value: 3L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "StatValue1",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "StatValue1",
                value: 3L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "StatValue1",
                value: 3L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "StatValue1",
                value: 2L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                column: "StatValue1",
                value: 3L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                column: "StatValue1",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                column: "StatValue1",
                value: 3L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                column: "StatValue1",
                value: 2L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_BaseAttackTime",
                table: "CreatureTemplates",
                sql: "\"BaseAttackTime\" >= 0.5 AND \"BaseAttackTime\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_HasteCap",
                table: "CombatFormula",
                sql: "\"HasteCap\" >= 0 AND \"HasteCap\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_MoveSpeedCap",
                table: "CombatFormula",
                sql: "\"MoveSpeedCap\" > -100 AND \"MoveSpeedCap\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_MoveSpeedFloor",
                table: "CombatFormula",
                sql: "\"MoveSpeedFloor\" > -100 AND \"MoveSpeedFloor\" <= \"MoveSpeedCap\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_BaseAttackTime",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_HasteCap",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_MoveSpeedCap",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_MoveSpeedFloor",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "HasteCap",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "MoveSpeedCap",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "MoveSpeedFloor",
                table: "CombatFormula");

            migrationBuilder.AlterColumn<int>(
                name: "BaseAttackTime",
                table: "CreatureTemplates",
                type: "integer",
                nullable: false,
                oldClrType: typeof(float),
                oldType: "real");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                column: "BaseAttackTime",
                value: 1);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "StatValue1",
                value: 13L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "StatValue1",
                value: 18L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "StatValue1",
                value: 15L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "StatValue1",
                value: 13L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "StatValue1",
                value: 15L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                column: "StatValue1",
                value: 13L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                column: "StatValue1",
                value: 18L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                column: "StatValue1",
                value: 15L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                column: "StatValue1",
                value: 15L);
        }
    }
}
