using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddCombatScalingAndCreatureDefence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "BlockPct",
                table: "CreatureRarityModifiers",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "CritPct",
                table: "CreatureRarityModifiers",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "DodgePct",
                table: "CreatureRarityModifiers",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<long>(
                name: "Armor",
                table: "CreatureBaseStats",
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

            migrationBuilder.AddColumn<float>(
                name: "WeaponCoefficient",
                table: "AbilityTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.3f, (byte)0, 1f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 201L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.6f, (byte)0, 1.5f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 202L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.5f, (byte)0, 1f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.25f, (byte)1, 0f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 211L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.8f, (byte)1, 0f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 212L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.5f, (byte)1, 0f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.3f, (byte)0, 1f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 221L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.6f, (byte)0, 1.2f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 222L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.4f, (byte)0, 0.8f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.3f, (byte)1, 0f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 231L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.5f, (byte)1, 0f });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 232L,
                columns: new[] { "ScalingCoefficient", "ScalingStat", "WeaponCoefficient" },
                values: new object[] { 0.6f, (byte)1, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 1,
                column: "Armor",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 2,
                column: "Armor",
                value: 3L);

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 3,
                column: "Armor",
                value: 7L);

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 4,
                column: "Armor",
                value: 10L);

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 5,
                column: "Armor",
                value: 13L);

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 6,
                column: "Armor",
                value: 17L);

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 7,
                column: "Armor",
                value: 20L);

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 8,
                column: "Armor",
                value: 23L);

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 9,
                column: "Armor",
                value: 27L);

            migrationBuilder.UpdateData(
                table: "CreatureBaseStats",
                keyColumn: "Level",
                keyValue: 10,
                column: "Armor",
                value: 30L);

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 0,
                columns: new[] { "BlockPct", "CritPct", "DodgePct" },
                values: new object[] { 0f, 0f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 1,
                columns: new[] { "BlockPct", "CritPct", "DodgePct" },
                values: new object[] { 0f, 5f, 3f });

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 2,
                columns: new[] { "BlockPct", "CritPct", "DodgePct" },
                values: new object[] { 5f, 8f, 5f });

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 3,
                columns: new[] { "BlockPct", "CritPct", "DodgePct" },
                values: new object[] { 10f, 10f, 5f });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 14L, 9L });

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

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 11L, 7L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 7L, 4L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 7L, 4L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 7L, 4L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 7L, 4L });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_ScalingCoefficient_NonNegative",
                table: "AbilityTemplates",
                sql: "\"ScalingCoefficient\" >= 0 AND \"ScalingCoefficient\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_WeaponCoefficient_NonNegative",
                table: "AbilityTemplates",
                sql: "\"WeaponCoefficient\" >= 0 AND \"WeaponCoefficient\" < 'Infinity'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_ScalingCoefficient_NonNegative",
                table: "AbilityTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_WeaponCoefficient_NonNegative",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "BlockPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropColumn(
                name: "CritPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropColumn(
                name: "DodgePct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropColumn(
                name: "Armor",
                table: "CreatureBaseStats");

            migrationBuilder.DropColumn(
                name: "ScalingCoefficient",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "ScalingStat",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "WeaponCoefficient",
                table: "AbilityTemplates");

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 5L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 4L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 4L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 4L, 2L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 32m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 2L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 33m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 3L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 34m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 2L, 1L });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 35m,
                columns: new[] { "DamageMax1", "DamageMin1" },
                values: new object[] { 2L, 1L });
        }
    }
}
