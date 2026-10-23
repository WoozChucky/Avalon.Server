using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddDefenceLayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // R1: the stored item damage types move to the new numbering (Physical 0, Fire 1, Cold 2, Lightning 3,
            // Blight 4): the former Poison 1 becomes Blight 4 and Fire 2 becomes 1; Lightning 3 stays. One statement per
            // column, so no row is remapped twice.
            migrationBuilder.Sql(
                "UPDATE \"ItemTemplates\" SET \"DamageType1\" = CASE \"DamageType1\" WHEN 1 THEN 4 WHEN 2 THEN 1 END WHERE \"DamageType1\" IN (1, 2);");
            migrationBuilder.Sql(
                "UPDATE \"ItemTemplates\" SET \"DamageType2\" = CASE \"DamageType2\" WHEN 1 THEN 4 WHEN 2 THEN 1 END WHERE \"DamageType2\" IN (1, 2);");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers");

            migrationBuilder.AddColumn<int>(
                name: "RequiredAgility",
                table: "ItemTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredIntellect",
                table: "ItemTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredStrength",
                table: "ItemTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "BlightResistance",
                table: "CreatureTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "ColdResistance",
                table: "CreatureTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "EvasionModifier",
                table: "CreatureTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "FireResistance",
                table: "CreatureTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "LightningResistance",
                table: "CreatureTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "WardModifier",
                table: "CreatureTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "DefenceMultiplier",
                table: "CreatureRarityModifiers",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<long>(
                name: "Evasion",
                table: "CreatureBaseStats",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Ward",
                table: "CreatureBaseStats",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<float>(
                name: "ArmorPerStrength",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "EvasionBase",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "EvasionCap",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "EvasionPerAgility",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "EvasionPerLevel",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "ResistanceCap",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "ResistanceFloor",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "WardPerIntellect",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "DamageType",
                table: "AbilityTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                column: "DamageType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 201L,
                column: "DamageType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 202L,
                column: "DamageType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 203L,
                column: "DamageType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 204L,
                column: "DamageType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                columns: new[] { "DamageType", "Name" },
                values: new object[] { 3, "Spark Bolt" });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 211L,
                column: "DamageType",
                value: 1);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 212L,
                column: "DamageType",
                value: 2);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 213L,
                column: "DamageType",
                value: 1);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 214L,
                column: "DamageType",
                value: 2);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                column: "DamageType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 221L,
                column: "DamageType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 222L,
                column: "DamageType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 223L,
                column: "DamageType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 224L,
                column: "DamageType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                column: "DamageType",
                value: 3);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 231L,
                column: "DamageType",
                value: 3);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 232L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 233L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 234L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 235L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 300L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 301L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 302L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 303L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 304L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 305L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 306L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 307L,
                column: "DamageType",
                value: 4);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 308L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 309L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 310L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 311L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 312L,
                column: "DamageType",
                value: 4);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 313L,
                column: "DamageType",
                value: 4);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 314L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 315L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 316L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 317L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 318L,
                column: "DamageType",
                value: null);

            migrationBuilder.UpdateData(
                table: "CombatFormula",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ArmorPerStrength", "EvasionBase", "EvasionCap", "EvasionPerAgility", "EvasionPerLevel", "ResistanceCap", "ResistanceFloor", "WardPerIntellect" },
                values: new object[] { 0.1f, 560f, 30f, 1.125f, 40f, 75f, -100f, 0.25f });

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
                column: "DefenceMultiplier",
                value: 1f);

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 1,
                column: "DefenceMultiplier",
                value: 1f);

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 2,
                column: "DefenceMultiplier",
                value: 1f);

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 3,
                column: "DefenceMultiplier",
                value: 1f);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "BlightResistance", "ColdResistance", "DmgSchool", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, (short)4, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                columns: new[] { "BlightResistance", "ColdResistance", "DmgSchool", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, (short)4, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                columns: new[] { "BlightResistance", "ColdResistance", "EvasionModifier", "FireResistance", "LightningResistance", "WardModifier" },
                values: new object[] { 0f, 0f, 0f, 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 15m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 16m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 17m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 18m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 19m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 20m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 21m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 22m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 23m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 24m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 25m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 26m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 27m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 28m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 29m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 30m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 31m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 36m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 37m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 38m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 39m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 40m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 41m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 42m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 43m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 44m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 45m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 46m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 47m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 48m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 49m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 50m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 51m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 52m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 53m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 54m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 55m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 56m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 57m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 58m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 59m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 60m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 61m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 62m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 63m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 64m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 65m,
                columns: new[] { "RequiredAgility", "RequiredIntellect", "RequiredStrength" },
                values: new object[] { null, null, null });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_BlightResistance",
                table: "CreatureTemplates",
                sql: "\"BlightResistance\" >= -100 AND \"BlightResistance\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_ColdResistance",
                table: "CreatureTemplates",
                sql: "\"ColdResistance\" >= -100 AND \"ColdResistance\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_DamageType",
                table: "CreatureTemplates",
                sql: "\"DmgSchool\" BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_EvasionModifier",
                table: "CreatureTemplates",
                sql: "\"EvasionModifier\" >= 0 AND \"EvasionModifier\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_FireResistance",
                table: "CreatureTemplates",
                sql: "\"FireResistance\" >= -100 AND \"FireResistance\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_LightningResistance",
                table: "CreatureTemplates",
                sql: "\"LightningResistance\" >= -100 AND \"LightningResistance\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_WardModifier",
                table: "CreatureTemplates",
                sql: "\"WardModifier\" >= 0 AND \"WardModifier\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureRarityModifiers_DefenceMultiplier",
                table: "CreatureRarityModifiers",
                sql: "\"DefenceMultiplier\" >= 0 AND \"DefenceMultiplier\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_ArmorPerStrength",
                table: "CombatFormula",
                sql: "\"ArmorPerStrength\" >= 0 AND \"ArmorPerStrength\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_EvasionBase",
                table: "CombatFormula",
                sql: "\"EvasionBase\" >= 0 AND \"EvasionBase\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_EvasionCap",
                table: "CombatFormula",
                sql: "\"EvasionCap\" >= 0 AND \"EvasionCap\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_EvasionPerAgility",
                table: "CombatFormula",
                sql: "\"EvasionPerAgility\" >= 0 AND \"EvasionPerAgility\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_EvasionPerLevel",
                table: "CombatFormula",
                sql: "\"EvasionPerLevel\" >= 0 AND \"EvasionPerLevel\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_EvasionTermsPositive",
                table: "CombatFormula",
                sql: "\"EvasionBase\" + \"EvasionPerLevel\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_ResistanceCap",
                table: "CombatFormula",
                sql: "\"ResistanceCap\" >= 0 AND \"ResistanceCap\" <= 90");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_ResistanceFloor",
                table: "CombatFormula",
                sql: "\"ResistanceFloor\" >= -100 AND \"ResistanceFloor\" <= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_WardPerIntellect",
                table: "CombatFormula",
                sql: "\"WardPerIntellect\" >= 0 AND \"WardPerIntellect\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers",
                sql: "\"Stat\" BETWEEN 1 AND 15");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_DamageType",
                table: "AbilityTemplates",
                sql: "\"DamageType\" IS NULL OR \"DamageType\" BETWEEN 0 AND 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_BlightResistance",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_ColdResistance",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_DamageType",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_EvasionModifier",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_FireResistance",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_LightningResistance",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_WardModifier",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureRarityModifiers_DefenceMultiplier",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_ArmorPerStrength",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_EvasionBase",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_EvasionCap",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_EvasionPerAgility",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_EvasionPerLevel",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_EvasionTermsPositive",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_ResistanceCap",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_ResistanceFloor",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_WardPerIntellect",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_DamageType",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "RequiredAgility",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "RequiredIntellect",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "RequiredStrength",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "BlightResistance",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "ColdResistance",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "EvasionModifier",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "FireResistance",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "LightningResistance",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "WardModifier",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "DefenceMultiplier",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropColumn(
                name: "Evasion",
                table: "CreatureBaseStats");

            migrationBuilder.DropColumn(
                name: "Ward",
                table: "CreatureBaseStats");

            migrationBuilder.DropColumn(
                name: "ArmorPerStrength",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "EvasionBase",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "EvasionCap",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "EvasionPerAgility",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "EvasionPerLevel",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "ResistanceCap",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "ResistanceFloor",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "WardPerIntellect",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "DamageType",
                table: "AbilityTemplates");

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                column: "Name",
                value: "Arcane Bolt");

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "DmgSchool",
                value: (short)0);

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "DmgSchool",
                value: (short)0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers",
                sql: "\"Stat\" BETWEEN 1 AND 10");

            // R1, reversed: Blight 4 back to the former Poison 1, Fire 1 back to 2.
            migrationBuilder.Sql(
                "UPDATE \"ItemTemplates\" SET \"DamageType1\" = CASE \"DamageType1\" WHEN 4 THEN 1 WHEN 1 THEN 2 END WHERE \"DamageType1\" IN (4, 1);");
            migrationBuilder.Sql(
                "UPDATE \"ItemTemplates\" SET \"DamageType2\" = CASE \"DamageType2\" WHEN 4 THEN 1 WHEN 1 THEN 2 END WHERE \"DamageType2\" IN (4, 1);");
        }
    }
}
