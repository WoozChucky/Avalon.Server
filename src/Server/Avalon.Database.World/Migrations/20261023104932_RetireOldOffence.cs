using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class RetireOldOffence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_DamageType",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassStatFactors_AbilityPerIntellect",
                table: "ClassStatFactors");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassStatFactors_AttackPerAgility",
                table: "ClassStatFactors");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassStatFactors_AttackPerStrength",
                table: "ClassStatFactors");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraTemplates_BaseDamageCoefficient",
                table: "AuraTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraTemplates_ScalingCoefficient",
                table: "AuraTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraTemplates_ScalingStat",
                table: "AuraTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_BaseDamageCoefficient_NonNegative",
                table: "AbilityTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_ScalingCoefficient_NonNegative",
                table: "AbilityTemplates");

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 9L, (byte)2 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 9L, (byte)3 });

            migrationBuilder.DropColumn(
                name: "AbilityPerIntellect",
                table: "ClassStatFactors");

            migrationBuilder.DropColumn(
                name: "AttackPerAgility",
                table: "ClassStatFactors");

            migrationBuilder.DropColumn(
                name: "AttackPerStrength",
                table: "ClassStatFactors");

            migrationBuilder.DropColumn(
                name: "BaseDamageCoefficient",
                table: "AuraTemplates");

            migrationBuilder.DropColumn(
                name: "ScalingCoefficient",
                table: "AuraTemplates");

            migrationBuilder.DropColumn(
                name: "ScalingStat",
                table: "AuraTemplates");

            migrationBuilder.DropColumn(
                name: "BaseDamageCoefficient",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "EffectValue",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "ScalingCoefficient",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "ScalingStat",
                table: "AbilityTemplates");

            migrationBuilder.RenameColumn(
                name: "DmgSchool",
                table: "CreatureTemplates",
                newName: "DamageType");

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 2L,
                column: "PeriodicBase",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 4L,
                column: "PeriodicBase",
                value: 0f);

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 6L, 4L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 7L, 5L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 7L, 5L });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_DamageType",
                table: "CreatureTemplates",
                sql: "\"DamageType\" BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers",
                sql: "\"Stat\" BETWEEN 1 AND 32 AND \"Stat\" NOT IN (2, 3, 7)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_DamageType",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers");

            migrationBuilder.RenameColumn(
                name: "DamageType",
                table: "CreatureTemplates",
                newName: "DmgSchool");

            migrationBuilder.AddColumn<double>(
                name: "AbilityPerIntellect",
                table: "ClassStatFactors",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "AttackPerAgility",
                table: "ClassStatFactors",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "AttackPerStrength",
                table: "ClassStatFactors",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<float>(
                name: "BaseDamageCoefficient",
                table: "AuraTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "ScalingCoefficient",
                table: "AuraTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<byte>(
                name: "ScalingStat",
                table: "AuraTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<float>(
                name: "BaseDamageCoefficient",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<long>(
                name: "EffectValue",
                table: "AbilityTemplates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<float>(
                name: "ScalingCoefficient",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<byte>(
                name: "ScalingStat",
                table: "AbilityTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1f, 12L, 0.3f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 201L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1.5f, 25L, 0.6f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 202L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1f, 20L, 0.5f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 203L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0.5f, 8L, 0.2f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 204L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0.3f, 6L, 0.15f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 12L, 0.25f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 211L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 18L, 0.4f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 212L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 22L, 0.5f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 213L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0L, 0f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 214L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 6L, 0.1f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1f, 12L, 0.3f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 221L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1.2f, 25L, 0.6f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 222L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0.8f, 22L, 0.4f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 223L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0.6f, 14L, 0.35f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 224L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0.3f, 8L, 0.2f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 12L, 0.3f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 231L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 22L, 0.5f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 232L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 40L, 0.6f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 233L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0L, 0f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 234L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0L, 0f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 235L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0L, 0f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 300L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 301L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1.6f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 302L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 303L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1.8f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 304L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 305L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1.4f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 306L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 307L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1.5f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 308L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 309L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1.8f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 310L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 2f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 311L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 312L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 2.4f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 313L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1.6f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 314L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 315L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 2.5f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 316L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1.8f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 317L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0.6f, 0L, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 318L,
                columns: new[] { "BaseDamageCoefficient", "EffectValue", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0L, 0f, (byte)0 });

            migrationBuilder.InsertData(
                table: "AuraStatModifiers",
                columns: new[] { "AuraId", "Stat", "Kind", "Value" },
                values: new object[,]
                {
                    { 9L, (byte)2, (byte)2, 10f },
                    { 9L, (byte)3, (byte)2, 10f }
                });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 1L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0.25f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 2L,
                columns: new[] { "BaseDamageCoefficient", "PeriodicBase", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 24f, 0.6f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 3L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 4L,
                columns: new[] { "BaseDamageCoefficient", "PeriodicBase", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 24f, 0.4f, (byte)1 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 5L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 6L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 1f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 7L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 8L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 9L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 10L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 11L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 12L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 13L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 14L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 15L,
                columns: new[] { "BaseDamageCoefficient", "ScalingCoefficient", "ScalingStat" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)1,
                columns: new[] { "AbilityPerIntellect", "AttackPerAgility", "AttackPerStrength" },
                values: new object[] { 0.20000000000000001, 0.0, 2.0 });

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)2,
                columns: new[] { "AbilityPerIntellect", "AttackPerAgility", "AttackPerStrength" },
                values: new object[] { 3.0, 0.0, 0.5 });

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)3,
                columns: new[] { "AbilityPerIntellect", "AttackPerAgility", "AttackPerStrength" },
                values: new object[] { 0.5, 1.5, 0.5 });

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)4,
                columns: new[] { "AbilityPerIntellect", "AttackPerAgility", "AttackPerStrength" },
                values: new object[] { 2.0, 0.0, 0.5 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 3L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 11L, 7L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 11L, 7L });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_DamageType",
                table: "CreatureTemplates",
                sql: "\"DmgSchool\" BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassStatFactors_AbilityPerIntellect",
                table: "ClassStatFactors",
                sql: "\"AbilityPerIntellect\" >= 0 AND \"AbilityPerIntellect\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassStatFactors_AttackPerAgility",
                table: "ClassStatFactors",
                sql: "\"AttackPerAgility\" >= 0 AND \"AttackPerAgility\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassStatFactors_AttackPerStrength",
                table: "ClassStatFactors",
                sql: "\"AttackPerStrength\" >= 0 AND \"AttackPerStrength\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraTemplates_BaseDamageCoefficient",
                table: "AuraTemplates",
                sql: "\"BaseDamageCoefficient\" >= 0 AND \"BaseDamageCoefficient\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraTemplates_ScalingCoefficient",
                table: "AuraTemplates",
                sql: "\"ScalingCoefficient\" >= 0 AND \"ScalingCoefficient\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraTemplates_ScalingStat",
                table: "AuraTemplates",
                sql: "\"ScalingStat\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraStatModifiers_Stat",
                table: "AuraStatModifiers",
                sql: "\"Stat\" BETWEEN 1 AND 32");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_BaseDamageCoefficient_NonNegative",
                table: "AbilityTemplates",
                sql: "\"BaseDamageCoefficient\" >= 0 AND \"BaseDamageCoefficient\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_ScalingCoefficient_NonNegative",
                table: "AbilityTemplates",
                sql: "\"ScalingCoefficient\" >= 0 AND \"ScalingCoefficient\" < 'Infinity'");
        }
    }
}
