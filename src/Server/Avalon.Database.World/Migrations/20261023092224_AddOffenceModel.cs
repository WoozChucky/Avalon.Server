using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddOffenceModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraStatModifiers_Kind",
                table: "AuraStatModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers");

            migrationBuilder.AddColumn<long>(
                name: "AttackTimeMs",
                table: "ItemTemplates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "AttackSpeedCap",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "AttackSpeedPerAgility",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BaseCritPct",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "CastSpeedCap",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "CooldownRecoveryCap",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "IncreasedHealingPerIntellect",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "IncreasedPhysicalAttackPerStrength",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "IncreasedSpellPerIntellect",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<long>(
                name: "LifePerStamina",
                table: "CombatFormula",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UnarmedAttackTimeMs",
                table: "CombatFormula",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UnarmedDamageMax",
                table: "CombatFormula",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UnarmedDamageMin",
                table: "CombatFormula",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<float>(
                name: "PeriodicEffectiveness",
                table: "AuraTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<long>(
                name: "AddedMax",
                table: "AbilityTemplates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "AddedMin",
                table: "AbilityTemplates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "AddedType",
                table: "AbilityTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "ConvertPct",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "ConvertToType",
                table: "AbilityTemplates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "DamageEffectiveness",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<long>(
                name: "SpellBaseMax",
                table: "AbilityTemplates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SpellBaseMin",
                table: "AbilityTemplates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<float>(
                name: "SpellBasePerLevelPct",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<int>(
                name: "Tags",
                table: "AbilityTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 315f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 201L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 614f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 202L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 492f, 0L, 0L, 0f, 17 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 203L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 202f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 204L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 148f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 20L, 14L, 0.7f, 18 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 211L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 32L, 21L, 0.75f, 10 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 212L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 40L, 26L, 0.8f, 10 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 213L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 46L, 31L, 0.9f, 10 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 214L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 9L, 6L, 0.45f, 10 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 363f, 0L, 0L, 0f, 17 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 221L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 690f, 0L, 0L, 0f, 17 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 222L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 516f, 0L, 0L, 0f, 9 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 223L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 50f, 2, 495f, 0L, 0L, 0f, 17 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 224L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 4L, 2L, 4, 0f, null, 168f, 0L, 0L, 0f, 17 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 17L, 12L, 0.45f, 18 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 231L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 31L, 20L, 0.35f, 10 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 232L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 44L, 30L, 0f, 12 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 233L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 28L, 19L, 0f, 12 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 234L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 0L, 0L, 0f, 8 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 235L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 0L, 0L, 0f, 16 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 300L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 301L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 160f, 0L, 0L, 0f, 9 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 302L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 303L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 180f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 304L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 305L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 140f, 0L, 0L, 0f, 17 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 306L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 307L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 150f, 0L, 0L, 0f, 9 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 308L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 309L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 180f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 310L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 200f, 0L, 0L, 0f, 9 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 311L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 312L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 240f, 0L, 0L, 0f, 9 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 313L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 160f, 0L, 0L, 0f, 17 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 314L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 100f, 0L, 0L, 0f, 41 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 315L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 250f, 0L, 0L, 0f, 9 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 316L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 180f, 0L, 0L, 0f, 9 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 317L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 60f, 0L, 0L, 0f, 17 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 318L,
                columns: new[] { "AddedMax", "AddedMin", "AddedType", "ConvertPct", "ConvertToType", "DamageEffectiveness", "SpellBaseMax", "SpellBaseMin", "SpellBasePerLevelPct", "Tags" },
                values: new object[] { 0L, 0L, null, 0f, null, 0f, 0L, 0L, 0f, 9 });

            migrationBuilder.InsertData(
                table: "AuraStatModifiers",
                columns: new[] { "AuraId", "Stat", "Kind", "Value" },
                values: new object[,]
                {
                    { 9L, (byte)16, (byte)1, 10f },
                    { 9L, (byte)28, (byte)1, 10f }
                });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 1L,
                column: "PeriodicEffectiveness",
                value: 21f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 2L,
                column: "PeriodicEffectiveness",
                value: 100f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 3L,
                column: "PeriodicEffectiveness",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 4L,
                column: "PeriodicEffectiveness",
                value: 100f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 5L,
                column: "PeriodicEffectiveness",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 6L,
                column: "PeriodicEffectiveness",
                value: 100f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 7L,
                column: "PeriodicEffectiveness",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 8L,
                column: "PeriodicEffectiveness",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 9L,
                column: "PeriodicEffectiveness",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 10L,
                column: "PeriodicEffectiveness",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 11L,
                column: "PeriodicEffectiveness",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 12L,
                column: "PeriodicEffectiveness",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 13L,
                column: "PeriodicEffectiveness",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 14L,
                column: "PeriodicEffectiveness",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 15L,
                column: "PeriodicEffectiveness",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "CombatFormula",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AttackSpeedCap", "AttackSpeedPerAgility", "BaseCritPct", "CastSpeedCap", "CooldownRecoveryCap", "IncreasedHealingPerIntellect", "IncreasedPhysicalAttackPerStrength", "IncreasedSpellPerIntellect", "LifePerStamina", "UnarmedAttackTimeMs", "UnarmedDamageMax", "UnarmedDamageMin" },
                values: new object[] { 50f, 0.1f, 5f, 50f, 50f, 3.5f, 3.5f, 3.5f, 5L, 816L, 6L, 3L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                column: "AttackTimeMs",
                value: 792L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                column: "AttackTimeMs",
                value: 800L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                column: "AttackTimeMs",
                value: 801L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                column: "AttackTimeMs",
                value: 792L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                column: "AttackTimeMs",
                value: 784L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 15m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 16m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 17m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 18m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 19m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 20m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 21m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 22m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 23m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 24m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 25m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 26m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 27m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 28m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 29m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 30m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 31m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                column: "AttackTimeMs",
                value: 792L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                column: "AttackTimeMs",
                value: 800L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                column: "AttackTimeMs",
                value: 801L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                column: "AttackTimeMs",
                value: 784L);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 36m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 37m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 38m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 39m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 40m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 41m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 42m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 43m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 44m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 45m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 46m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 47m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 48m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 49m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 50m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 51m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 52m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 53m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 54m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 55m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 56m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 57m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 58m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 59m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 60m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 61m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 62m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 63m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 64m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 65m,
                column: "AttackTimeMs",
                value: null);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ItemTemplates_AttackTimeMs",
                table: "ItemTemplates",
                sql: "\"AttackTimeMs\" IS NULL OR \"AttackTimeMs\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_AttackSpeedCap",
                table: "CombatFormula",
                sql: "\"AttackSpeedCap\" >= 0 AND \"AttackSpeedCap\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_AttackSpeedPerAgility",
                table: "CombatFormula",
                sql: "\"AttackSpeedPerAgility\" >= 0 AND \"AttackSpeedPerAgility\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_BaseCritPct",
                table: "CombatFormula",
                sql: "\"BaseCritPct\" >= 0 AND \"BaseCritPct\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_CastSpeedCap",
                table: "CombatFormula",
                sql: "\"CastSpeedCap\" >= 0 AND \"CastSpeedCap\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_CooldownRecoveryCap",
                table: "CombatFormula",
                sql: "\"CooldownRecoveryCap\" >= 0 AND \"CooldownRecoveryCap\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_IncreasedHealingPerIntellect",
                table: "CombatFormula",
                sql: "\"IncreasedHealingPerIntellect\" >= 0 AND \"IncreasedHealingPerIntellect\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_IncreasedPhysicalAttackPerStrength",
                table: "CombatFormula",
                sql: "\"IncreasedPhysicalAttackPerStrength\" >= 0 AND \"IncreasedPhysicalAttackPerStrength\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_IncreasedSpellPerIntellect",
                table: "CombatFormula",
                sql: "\"IncreasedSpellPerIntellect\" >= 0 AND \"IncreasedSpellPerIntellect\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_LifePerStamina",
                table: "CombatFormula",
                sql: "\"LifePerStamina\" >= 0 AND \"LifePerStamina\" <= 4294967295");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_Unarmed",
                table: "CombatFormula",
                sql: "\"UnarmedDamageMin\" <= \"UnarmedDamageMax\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_UnarmedAttackTimeMs",
                table: "CombatFormula",
                sql: "\"UnarmedAttackTimeMs\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraTemplates_PeriodicEffectiveness",
                table: "AuraTemplates",
                sql: "\"PeriodicEffectiveness\" >= 0 AND \"PeriodicEffectiveness\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraStatModifiers_Kind",
                table: "AuraStatModifiers",
                sql: "\"Kind\" IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers",
                sql: "\"Stat\" BETWEEN 1 AND 32");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_Added",
                table: "AbilityTemplates",
                sql: "\"AddedMin\" <= \"AddedMax\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_AddedType",
                table: "AbilityTemplates",
                sql: "\"AddedType\" IS NULL OR \"AddedType\" BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_ConvertPct",
                table: "AbilityTemplates",
                sql: "\"ConvertPct\" >= 0 AND \"ConvertPct\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_ConvertToType",
                table: "AbilityTemplates",
                sql: "\"ConvertToType\" IS NULL OR \"ConvertToType\" BETWEEN 1 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_DamageEffectiveness",
                table: "AbilityTemplates",
                sql: "\"DamageEffectiveness\" >= 0 AND \"DamageEffectiveness\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_SpellBase",
                table: "AbilityTemplates",
                sql: "\"SpellBaseMin\" <= \"SpellBaseMax\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_SpellBasePerLevelPct",
                table: "AbilityTemplates",
                sql: "\"SpellBasePerLevelPct\" >= 0 AND \"SpellBasePerLevelPct\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_Tags",
                table: "AbilityTemplates",
                sql: "\"Tags\" BETWEEN 0 AND 127");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ItemTemplates_AttackTimeMs",
                table: "ItemTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_AttackSpeedCap",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_AttackSpeedPerAgility",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_BaseCritPct",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_CastSpeedCap",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_CooldownRecoveryCap",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_IncreasedHealingPerIntellect",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_IncreasedPhysicalAttackPerStrength",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_IncreasedSpellPerIntellect",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_LifePerStamina",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_Unarmed",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_UnarmedAttackTimeMs",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraTemplates_PeriodicEffectiveness",
                table: "AuraTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraStatModifiers_Kind",
                table: "AuraStatModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_Added",
                table: "AbilityTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_AddedType",
                table: "AbilityTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_ConvertPct",
                table: "AbilityTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_ConvertToType",
                table: "AbilityTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_DamageEffectiveness",
                table: "AbilityTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_SpellBase",
                table: "AbilityTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_SpellBasePerLevelPct",
                table: "AbilityTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_Tags",
                table: "AbilityTemplates");

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 9L, (byte)16 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 9L, (byte)28 });

            migrationBuilder.DropColumn(
                name: "AttackTimeMs",
                table: "ItemTemplates");

            migrationBuilder.DropColumn(
                name: "AttackSpeedCap",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "AttackSpeedPerAgility",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "BaseCritPct",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "CastSpeedCap",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "CooldownRecoveryCap",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "IncreasedHealingPerIntellect",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "IncreasedPhysicalAttackPerStrength",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "IncreasedSpellPerIntellect",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "LifePerStamina",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "UnarmedAttackTimeMs",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "UnarmedDamageMax",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "UnarmedDamageMin",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "PeriodicEffectiveness",
                table: "AuraTemplates");

            migrationBuilder.DropColumn(
                name: "AddedMax",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "AddedMin",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "AddedType",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "ConvertPct",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "ConvertToType",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "DamageEffectiveness",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "SpellBaseMax",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "SpellBaseMin",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "SpellBasePerLevelPct",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "AbilityTemplates");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraStatModifiers_Kind",
                table: "AuraStatModifiers",
                sql: "\"Kind\" IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers",
                sql: "\"Stat\" BETWEEN 1 AND 15");
        }
    }
}
