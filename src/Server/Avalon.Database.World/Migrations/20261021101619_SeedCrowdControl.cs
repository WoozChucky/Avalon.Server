using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedCrowdControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "RootDurationPct",
                table: "CreatureTemplates",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "SilenceDurationPct",
                table: "CreatureTemplates",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "SlowDurationPct",
                table: "CreatureTemplates",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "StunDurationPct",
                table: "CreatureTemplates",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "RootDurationPct",
                table: "CreatureRarityModifiers",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "SilenceDurationPct",
                table: "CreatureRarityModifiers",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "SlowDurationPct",
                table: "CreatureRarityModifiers",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "StunDurationPct",
                table: "CreatureRarityModifiers",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BreakOnDamagePct",
                table: "AuraTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "BreakPerSecondPct",
                table: "AuraTemplates",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<byte>(
                name: "Control",
                table: "AuraTemplates",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                column: "Flags",
                value: 4L);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                column: "Flags",
                value: 4L);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                column: "Flags",
                value: 4L);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                column: "Flags",
                value: 4L);

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 1L,
                columns: new[] { "BreakOnDamagePct", "BreakPerSecondPct", "Control" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 2L,
                columns: new[] { "BreakOnDamagePct", "BreakPerSecondPct", "Control" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 3L,
                columns: new[] { "BreakOnDamagePct", "BreakPerSecondPct", "Control" },
                values: new object[] { 0f, 0.5f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 4L,
                columns: new[] { "BreakOnDamagePct", "BreakPerSecondPct", "Control" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 5L,
                columns: new[] { "BreakOnDamagePct", "BreakPerSecondPct", "Control" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 6L,
                columns: new[] { "BreakOnDamagePct", "BreakPerSecondPct", "Control" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 7L,
                columns: new[] { "BreakOnDamagePct", "BreakPerSecondPct", "Control" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 8L,
                columns: new[] { "BreakOnDamagePct", "BreakPerSecondPct", "Control" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 9L,
                columns: new[] { "BreakOnDamagePct", "BreakPerSecondPct", "Control" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 10L,
                columns: new[] { "BreakOnDamagePct", "BreakPerSecondPct", "Control" },
                values: new object[] { 0f, 0.5f, (byte)0 });

            migrationBuilder.UpdateData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 11L,
                columns: new[] { "BreakOnDamagePct", "BreakPerSecondPct", "Control" },
                values: new object[] { 0f, 0f, (byte)0 });

            migrationBuilder.InsertData(
                table: "AuraTemplates",
                columns: new[] { "Id", "BaseDamageCoefficient", "BreakOnDamagePct", "BreakPerSecondPct", "Control", "DurationMs", "Icon", "Kind", "MaxStacks", "Name", "PeriodicBase", "PeriodicKind", "ScalingCoefficient", "ScalingStat", "ScriptName", "Stacking", "TickIntervalMs" },
                values: new object[,]
                {
                    { 12L, 0f, 10f, 0.5f, (byte)1, 1500L, "stunned", (byte)2, 1L, "Stunned", 0f, (byte)0, 0f, (byte)0, null, (byte)1, 0L },
                    { 13L, 0f, 25f, 0.5f, (byte)2, 3000L, "bound", (byte)2, 1L, "Bound", 0f, (byte)0, 0f, (byte)0, null, (byte)1, 0L },
                    { 14L, 0f, 25f, 0.5f, (byte)2, 2500L, "frozen", (byte)2, 1L, "Frozen", 0f, (byte)0, 0f, (byte)0, null, (byte)1, 0L },
                    { 15L, 0f, 0f, 0f, (byte)4, 3000L, "silenced", (byte)2, 1L, "Silenced", 0f, (byte)0, 0f, (byte)0, null, (byte)1, 0L }
                });

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)1,
                column: "StartingSpells",
                value: "200,201,202,203,204");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)2,
                column: "StartingSpells",
                value: "210,211,212,213,214");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)3,
                column: "StartingSpells",
                value: "220,221,222,223,224");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)4,
                column: "StartingSpells",
                value: "230,231,232,233,234,235");

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 0,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { 100f, 100f, 100f, 100f });

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 1,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { 100f, 100f, 100f, 100f });

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 2,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { 50f, 50f, 50f, 50f });

            migrationBuilder.UpdateData(
                table: "CreatureRarityModifiers",
                keyColumn: "Rarity",
                keyValue: 3,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { 50f, 0f, 50f, 0f });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 1m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 3m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 4m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 5m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 6m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 7m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 8m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 9m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 10m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 11m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 12m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 13m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "CreatureTemplates",
                keyColumn: "Id",
                keyValue: 14m,
                columns: new[] { "RootDurationPct", "SilenceDurationPct", "SlowDurationPct", "StunDurationPct" },
                values: new object[] { null, null, null, null });

            migrationBuilder.InsertData(
                table: "AbilityTemplates",
                columns: new[] { "Id", "Affects", "AimMode", "AllowedClasses", "Anchor", "AnimationId", "ArcDegrees", "AuraId", "BaseDamageCoefficient", "CastTime", "Cooldown", "Cost", "CostPowerType", "EffectValue", "Effects", "Flags", "HealThreatPerHp", "Name", "Pierce", "PowerGainPerHit", "ProjectileSpeed", "Radius", "Range", "Reach", "ScalingCoefficient", "ScalingStat", "ScriptName", "Shape", "TauntDurationMs", "ThreatMultiplier" },
                values: new object[,]
                {
                    { 204L, (byte)0, (byte)0, new byte[] { 1 }, (byte)0, 0L, 90f, 12L, 0.3f, 0L, 15000L, 15L, 2, 6L, 1, 0L, 0f, "Shield Bash", false, 0, 0f, 0f, 2, 2.5f, 0.15f, (byte)0, "ConeAbilityScript", (byte)1, 0L, 1f },
                    { 214L, (byte)0, (byte)1, new byte[] { 2 }, (byte)1, 0L, 0f, 14L, 0f, 0L, 20000L, 25L, 1, 6L, 1, 0L, 0f, "Frost Bind", false, 0, 0f, 3f, 20, 18f, 0.1f, (byte)1, "CircleAbilityScript", (byte)0, 0L, 1f },
                    { 224L, (byte)0, (byte)1, new byte[] { 3 }, (byte)0, 0L, 0f, 13L, 0.3f, 0L, 15000L, 20L, 3, 8L, 1, 0L, 0f, "Binding Shot", false, 0, 28f, 0f, 20, 25f, 0.2f, (byte)0, "ProjectileAbilityScript", (byte)2, 0L, 1f },
                    { 235L, (byte)0, (byte)1, new byte[] { 4 }, (byte)0, 0L, 0f, 15L, 0f, 0L, 20000L, 20L, 1, 6L, 1, 0L, 0f, "Hush", false, 0, 20f, 0f, 20, 18f, 0.1f, (byte)1, "ProjectileAbilityScript", (byte)2, 0L, 1f }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_RootDurationPct",
                table: "CreatureTemplates",
                sql: "\"RootDurationPct\" IS NULL OR (\"RootDurationPct\" >= 0 AND \"RootDurationPct\" <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_SilenceDurationPct",
                table: "CreatureTemplates",
                sql: "\"SilenceDurationPct\" IS NULL OR (\"SilenceDurationPct\" >= 0 AND \"SilenceDurationPct\" <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_SlowDurationPct",
                table: "CreatureTemplates",
                sql: "\"SlowDurationPct\" IS NULL OR (\"SlowDurationPct\" >= 0 AND \"SlowDurationPct\" <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureTemplates_StunDurationPct",
                table: "CreatureTemplates",
                sql: "\"StunDurationPct\" IS NULL OR (\"StunDurationPct\" >= 0 AND \"StunDurationPct\" <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureRarityModifiers_RootDurationPct",
                table: "CreatureRarityModifiers",
                sql: "\"RootDurationPct\" >= 0 AND \"RootDurationPct\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureRarityModifiers_SilenceDurationPct",
                table: "CreatureRarityModifiers",
                sql: "\"SilenceDurationPct\" >= 0 AND \"SilenceDurationPct\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureRarityModifiers_SlowDurationPct",
                table: "CreatureRarityModifiers",
                sql: "\"SlowDurationPct\" >= 0 AND \"SlowDurationPct\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatureRarityModifiers_StunDurationPct",
                table: "CreatureRarityModifiers",
                sql: "\"StunDurationPct\" >= 0 AND \"StunDurationPct\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraTemplates_BreakOnDamagePct",
                table: "AuraTemplates",
                sql: "\"BreakOnDamagePct\" >= 0 AND \"BreakOnDamagePct\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraTemplates_BreakPerSecondPct",
                table: "AuraTemplates",
                sql: "\"BreakPerSecondPct\" >= 0 AND \"BreakPerSecondPct\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraTemplates_Control",
                table: "AuraTemplates",
                sql: "\"Control\" BETWEEN 0 AND 7");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuraTemplates_ControlIsHarmful",
                table: "AuraTemplates",
                sql: "\"Control\" = 0 OR \"Kind\" = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_RootDurationPct",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_SilenceDurationPct",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_SlowDurationPct",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureTemplates_StunDurationPct",
                table: "CreatureTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureRarityModifiers_RootDurationPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureRarityModifiers_SilenceDurationPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureRarityModifiers_SlowDurationPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatureRarityModifiers_StunDurationPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraTemplates_BreakOnDamagePct",
                table: "AuraTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraTemplates_BreakPerSecondPct",
                table: "AuraTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraTemplates_Control",
                table: "AuraTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuraTemplates_ControlIsHarmful",
                table: "AuraTemplates");

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 204L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 214L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 224L);

            migrationBuilder.DeleteData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 235L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 12L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 13L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 14L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 15L);

            migrationBuilder.DropColumn(
                name: "RootDurationPct",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "SilenceDurationPct",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "SlowDurationPct",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "StunDurationPct",
                table: "CreatureTemplates");

            migrationBuilder.DropColumn(
                name: "RootDurationPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropColumn(
                name: "SilenceDurationPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropColumn(
                name: "SlowDurationPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropColumn(
                name: "StunDurationPct",
                table: "CreatureRarityModifiers");

            migrationBuilder.DropColumn(
                name: "BreakOnDamagePct",
                table: "AuraTemplates");

            migrationBuilder.DropColumn(
                name: "BreakPerSecondPct",
                table: "AuraTemplates");

            migrationBuilder.DropColumn(
                name: "Control",
                table: "AuraTemplates");

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                column: "Flags",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                column: "Flags",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                column: "Flags",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                column: "Flags",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)1,
                column: "StartingSpells",
                value: "200,201,202,203");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)2,
                column: "StartingSpells",
                value: "210,211,212,213");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)3,
                column: "StartingSpells",
                value: "220,221,222,223");

            migrationBuilder.UpdateData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)4,
                column: "StartingSpells",
                value: "230,231,232,233,234");
        }
    }
}
