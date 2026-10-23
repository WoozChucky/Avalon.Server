using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class ReexpressForestDefences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureRarityModifiers_DodgePct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_DodgeCap",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassStatFactors_BaseBlock",
                table: "ClassStatFactors");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassStatFactors_BaseDodge",
                table: "ClassStatFactors");

            migrationBuilder.DropColumn(
                name: "DodgePct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropColumn(
                name: "DodgeCap",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "BaseBlock",
                table: "ClassStatFactors");

            migrationBuilder.DropColumn(
                name: "BaseDodge",
                table: "ClassStatFactors");

            migrationBuilder.AlterColumn<float>(
                name: "DefenceMultiplier",
                table: "CreatureRarityModifiers",
                type: "real",
                nullable: false,
                defaultValue: 1f,
                oldClrType: typeof(float),
                oldType: "real");

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 1,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 10L, 8L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 2,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 20L, 16L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 3,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 30L, 24L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 4,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 40L, 32L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 5,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 50L, 40L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 6,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 60L, 48L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 7,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 70L, 56L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 8,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 80L, 64L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 9,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 90L, 72L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 10,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 100L, 80L });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "BlightResistance", "EvasionModifier" },
                values: new object[] { 25f, 0.5f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "BlightResistance", "FireResistance" },
                values: new object[] { 50f, -25f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "EvasionModifier",
                value: 0.55f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "EvasionModifier",
                value: 0.8f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                columns: new[] { "BlightResistance", "EvasionModifier", "WardModifier" },
                values: new object[] { 40f, 0.8f, 1f });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<float>(
                name: "DefenceMultiplier",
                table: "CreatureRarityModifiers",
                type: "real",
                nullable: false,
                oldClrType: typeof(float),
                oldType: "real",
                oldDefaultValue: 1f);

            migrationBuilder.AddColumn<float>(
                name: "DodgePct",
                table: "CreatureRarityModifiers",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "DodgeCap",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BaseBlock",
                table: "ClassStatFactors",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BaseDodge",
                table: "ClassStatFactors",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)1,
                columns: new[] { "BaseBlock", "BaseDodge" },
                values: new object[] { 5f, 3.664f });

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)2,
                columns: new[] { "BaseBlock", "BaseDodge" },
                values: new object[] { 0f, 3.25f });

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)3,
                columns: new[] { "BaseBlock", "BaseDodge" },
                values: new object[] { 0f, 4.35f });

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)4,
                columns: new[] { "BaseBlock", "BaseDodge" },
                values: new object[] { 0f, 3.25f });

            migrationBuilder.UpdateData(
                table: "CombatFormula",
                keyColumn: "Id",
                keyValue: 1,
                column: "DodgeCap",
                value: 30f);

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 1,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 0L, 0L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 2,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 0L, 0L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 3,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 0L, 0L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 4,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 0L, 0L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 5,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 0L, 0L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 6,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 0L, 0L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 7,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 0L, 0L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 8,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 0L, 0L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 9,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 0L, 0L });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 10,
                columns: new[] { "Evasion", "Ward" },
                values: new object[] { 0L, 0L });

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 0,
                column: "DodgePct",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 1,
                column: "DodgePct",
                value: 3f);

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 2,
                column: "DodgePct",
                value: 5f);

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 3,
                column: "DodgePct",
                value: 5f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "BlightResistance", "EvasionModifier" },
                values: new object[] { 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "BlightResistance", "FireResistance" },
                values: new object[] { 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "EvasionModifier",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "EvasionModifier",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                columns: new[] { "BlightResistance", "EvasionModifier", "WardModifier" },
                values: new object[] { 0f, 0f, 0f });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureRarityModifiers_DodgePct",
                table: "CreatureRarityModifiers",
                sql: "\"DodgePct\" >= 0 AND \"DodgePct\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_DodgeCap",
                table: "CombatFormula",
                sql: "\"DodgeCap\" >= 0 AND \"DodgeCap\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassStatFactors_BaseBlock",
                table: "ClassStatFactors",
                sql: "\"BaseBlock\" >= 0 AND \"BaseBlock\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassStatFactors_BaseDodge",
                table: "ClassStatFactors",
                sql: "\"BaseDodge\" >= 0 AND \"BaseDodge\" < 'Infinity'");
        }
    }
}
