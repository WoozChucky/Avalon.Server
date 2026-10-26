using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddResonanceEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemThemeEffects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ThemeId = table.Column<int>(type: "integer", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    Trigger = table.Column<byte>(type: "smallint", nullable: false),
                    ChancePct = table.Column<float>(type: "real", nullable: false),
                    CooldownMs = table.Column<long>(type: "bigint", nullable: false),
                    AuraId = table.Column<long>(type: "bigint", nullable: false),
                    Target = table.Column<byte>(type: "smallint", nullable: false),
                    ChainTargets = table.Column<int>(type: "integer", nullable: false),
                    AtOrBelowLifePct = table.Column<float>(type: "real", nullable: true),
                    DamageType = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemThemeEffects", x => x.Id);
                    table.CheckConstraint("CK_ItemThemeEffects_AtOrBelowLifePct", "\"AtOrBelowLifePct\" IS NULL OR (\"AtOrBelowLifePct\" > 0 AND \"AtOrBelowLifePct\" <= 100)");
                    table.CheckConstraint("CK_ItemThemeEffects_ChainTargets", "\"ChainTargets\" >= 0 AND (\"ChainTargets\" = 0 OR \"Target\" = 2)");
                    table.CheckConstraint("CK_ItemThemeEffects_ChancePct", "\"ChancePct\" > 0 AND \"ChancePct\" <= 100");
                    table.CheckConstraint("CK_ItemThemeEffects_CooldownMs", "\"CooldownMs\" > 0");
                    table.CheckConstraint("CK_ItemThemeEffects_Count", "\"Count\" >= 1");
                    table.CheckConstraint("CK_ItemThemeEffects_DamageType", "\"DamageType\" IS NULL OR \"DamageType\" BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_ItemThemeEffects_Target", "\"Target\" BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_ItemThemeEffects_Trigger", "\"Trigger\" BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "FK_ItemThemeEffects_AuraTemplates_AuraId",
                        column: x => x.AuraId,
                        principalTable: "AuraTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemThemeEffects_ItemThemes_ThemeId",
                        column: x => x.ThemeId,
                        principalTable: "ItemThemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AuraTemplates",
                columns: new[] { "Id", "BreakOnDamagePct", "BreakPerSecondPct", "Control", "DurationMs", "Icon", "Kind", "MaxStacks", "Name", "PeriodicBase", "PeriodicEffectiveness", "PeriodicKind", "ScriptName", "Stacking", "TickIntervalMs" },
                values: new object[,]
                {
                    { 16L, 0f, 0f, (byte)0, 4000L, "burning", (byte)2, 1L, "Burning I", 16f, 0f, (byte)1, null, (byte)3, 1000L },
                    { 17L, 0f, 0f, (byte)0, 4000L, "burning", (byte)2, 1L, "Burning II", 22f, 0f, (byte)1, null, (byte)3, 1000L },
                    { 18L, 0f, 0f, (byte)0, 4000L, "burning", (byte)2, 1L, "Burning III", 28f, 0f, (byte)1, null, (byte)3, 1000L },
                    { 19L, 0f, 0.5f, (byte)0, 3000L, "chilled", (byte)2, 1L, "Chilled I", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 20L, 0f, 0.5f, (byte)0, 3000L, "chilled", (byte)2, 1L, "Chilled II", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 21L, 0f, 0.5f, (byte)0, 3000L, "chilled", (byte)2, 1L, "Chilled III", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 22L, 25f, 0.5f, (byte)2, 1000L, "rimebound", (byte)2, 1L, "Rimebound", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 23L, 0f, 0f, (byte)0, 200L, "jolt", (byte)2, 1L, "Jolt I", 14f, 0f, (byte)1, null, (byte)3, 200L },
                    { 24L, 0f, 0f, (byte)0, 200L, "jolt", (byte)2, 1L, "Jolt II", 18f, 0f, (byte)1, null, (byte)3, 200L },
                    { 25L, 0f, 0f, (byte)0, 200L, "jolt", (byte)2, 1L, "Jolt III", 22f, 0f, (byte)1, null, (byte)3, 200L },
                    { 26L, 0f, 0f, (byte)0, 4000L, "regrowth", (byte)1, 1L, "Regrowth I", 16f, 0f, (byte)2, null, (byte)1, 1000L },
                    { 27L, 0f, 0f, (byte)0, 4000L, "regrowth", (byte)1, 1L, "Regrowth II", 24f, 0f, (byte)2, null, (byte)1, 1000L },
                    { 28L, 0f, 0f, (byte)0, 4000L, "regrowth", (byte)1, 1L, "Regrowth III", 32f, 0f, (byte)2, null, (byte)1, 1000L },
                    { 29L, 0f, 0f, (byte)0, 3000L, "rootsurge", (byte)1, 1L, "Rootsurge", 48f, 0f, (byte)2, null, (byte)1, 1000L },
                    { 30L, 0f, 0f, (byte)0, 4000L, "guard", (byte)1, 1L, "Guard I", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 31L, 0f, 0f, (byte)0, 4000L, "guard", (byte)1, 1L, "Guard II", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 32L, 0f, 0f, (byte)0, 4000L, "guard", (byte)1, 1L, "Guard III", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 33L, 0f, 0f, (byte)0, 200L, "barbs", (byte)2, 1L, "Barbs I", 5f, 0f, (byte)1, null, (byte)3, 200L },
                    { 34L, 0f, 0f, (byte)0, 200L, "barbs", (byte)2, 1L, "Barbs II", 8f, 0f, (byte)1, null, (byte)3, 200L },
                    { 35L, 0f, 0f, (byte)0, 200L, "barbs", (byte)2, 1L, "Barbs III", 10f, 0f, (byte)1, null, (byte)3, 200L },
                    { 36L, 0f, 0f, (byte)0, 4000L, "bleeding", (byte)2, 1L, "Bleeding", 20f, 0f, (byte)1, null, (byte)3, 1000L },
                    { 37L, 0f, 0f, (byte)0, 4000L, "tailwind", (byte)1, 1L, "Tailwind I", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 38L, 0f, 0f, (byte)0, 4000L, "tailwind", (byte)1, 1L, "Tailwind II", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 39L, 0f, 0f, (byte)0, 5000L, "tailwind", (byte)1, 1L, "Tailwind III", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 40L, 0f, 0f, (byte)0, 2000L, "slipstream", (byte)1, 1L, "Slipstream", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 41L, 0f, 0f, (byte)0, 3000L, "veiled", (byte)1, 1L, "Veiled I", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 42L, 0f, 0f, (byte)0, 3000L, "veiled", (byte)1, 1L, "Veiled II", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 43L, 0f, 0f, (byte)0, 4000L, "veiled", (byte)1, 1L, "Veiled III", 0f, 0f, (byte)0, null, (byte)1, 0L },
                    { 44L, 0f, 0f, (byte)0, 4000L, "lucid", (byte)1, 1L, "Lucid", 0f, 0f, (byte)0, null, (byte)1, 0L }
                });

            migrationBuilder.InsertData(
                table: "AuraStatModifiers",
                columns: new[] { "AuraId", "Stat", "Kind", "Value" },
                values: new object[,]
                {
                    { 19L, (byte)8, (byte)1, -15f },
                    { 20L, (byte)8, (byte)1, -20f },
                    { 20L, (byte)30, (byte)1, -8f },
                    { 21L, (byte)8, (byte)1, -25f },
                    { 21L, (byte)30, (byte)1, -12f },
                    { 30L, (byte)1, (byte)1, 15f },
                    { 30L, (byte)6, (byte)1, 2f },
                    { 31L, (byte)1, (byte)1, 25f },
                    { 31L, (byte)6, (byte)1, 4f },
                    { 32L, (byte)1, (byte)1, 35f },
                    { 32L, (byte)6, (byte)1, 5f },
                    { 32L, (byte)15, (byte)1, 25f },
                    { 37L, (byte)8, (byte)1, 10f },
                    { 38L, (byte)8, (byte)1, 12f },
                    { 38L, (byte)30, (byte)1, 5f },
                    { 38L, (byte)31, (byte)1, 5f },
                    { 39L, (byte)8, (byte)1, 15f },
                    { 39L, (byte)30, (byte)1, 8f },
                    { 39L, (byte)31, (byte)1, 8f },
                    { 40L, (byte)8, (byte)1, 20f },
                    { 41L, (byte)5, (byte)1, 60f },
                    { 42L, (byte)5, (byte)1, 100f },
                    { 42L, (byte)8, (byte)1, 10f },
                    { 43L, (byte)5, (byte)1, 150f },
                    { 43L, (byte)8, (byte)1, 15f },
                    { 44L, (byte)31, (byte)1, 10f }
                });

            migrationBuilder.InsertData(
                table: "ItemThemeEffects",
                columns: new[] { "Id", "AtOrBelowLifePct", "AuraId", "ChainTargets", "ChancePct", "CooldownMs", "Count", "DamageType", "Target", "ThemeId", "Trigger" },
                values: new object[,]
                {
                    { 1, null, 16L, 0, 20f, 2000L, 2, (short)1, (byte)2, 1, (byte)1 },
                    { 2, null, 17L, 0, 25f, 2000L, 3, (short)1, (byte)2, 1, (byte)1 },
                    { 3, null, 18L, 0, 30f, 2000L, 4, (short)1, (byte)2, 1, (byte)1 },
                    { 4, null, 19L, 0, 20f, 3000L, 2, null, (byte)2, 2, (byte)1 },
                    { 5, null, 20L, 0, 25f, 3000L, 3, null, (byte)2, 2, (byte)1 },
                    { 6, null, 21L, 0, 30f, 3000L, 4, null, (byte)2, 2, (byte)1 },
                    { 7, null, 22L, 0, 25f, 10000L, 4, null, (byte)2, 2, (byte)2 },
                    { 8, null, 23L, 0, 100f, 2000L, 2, (short)3, (byte)2, 3, (byte)2 },
                    { 9, null, 24L, 1, 100f, 2000L, 3, (short)3, (byte)2, 3, (byte)2 },
                    { 10, null, 25L, 2, 100f, 2000L, 4, (short)3, (byte)2, 3, (byte)2 },
                    { 11, null, 26L, 0, 100f, 4000L, 2, null, (byte)1, 4, (byte)3 },
                    { 12, null, 27L, 0, 100f, 4000L, 3, null, (byte)1, 4, (byte)3 },
                    { 13, null, 28L, 0, 100f, 4000L, 4, null, (byte)1, 4, (byte)3 },
                    { 14, 35f, 29L, 0, 100f, 20000L, 4, null, (byte)1, 4, (byte)4 },
                    { 15, null, 30L, 0, 20f, 6000L, 2, null, (byte)1, 5, (byte)4 },
                    { 16, null, 31L, 0, 25f, 6000L, 3, null, (byte)1, 5, (byte)4 },
                    { 17, null, 32L, 0, 30f, 6000L, 4, null, (byte)1, 5, (byte)4 },
                    { 18, null, 33L, 0, 100f, 2000L, 2, (short)0, (byte)3, 6, (byte)4 },
                    { 19, null, 34L, 0, 100f, 2000L, 3, (short)0, (byte)3, 6, (byte)4 },
                    { 20, null, 35L, 0, 100f, 2000L, 4, (short)0, (byte)3, 6, (byte)4 },
                    { 21, null, 36L, 0, 100f, 4000L, 4, (short)0, (byte)2, 6, (byte)2 },
                    { 22, null, 37L, 0, 100f, 5000L, 2, null, (byte)1, 7, (byte)3 },
                    { 23, null, 38L, 0, 100f, 5000L, 3, null, (byte)1, 7, (byte)3 },
                    { 24, null, 39L, 0, 100f, 5000L, 4, null, (byte)1, 7, (byte)3 },
                    { 25, null, 40L, 0, 100f, 8000L, 4, null, (byte)1, 7, (byte)5 },
                    { 26, null, 41L, 0, 100f, 12000L, 2, null, (byte)1, 8, (byte)6 },
                    { 27, null, 42L, 0, 100f, 12000L, 3, null, (byte)1, 8, (byte)6 },
                    { 28, null, 43L, 0, 100f, 12000L, 4, null, (byte)1, 8, (byte)6 },
                    { 29, null, 44L, 0, 100f, 8000L, 4, null, (byte)1, 8, (byte)2 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemThemeEffects_AuraId",
                table: "ItemThemeEffects",
                column: "AuraId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemThemeEffects_Theme_Count_Trigger",
                table: "ItemThemeEffects",
                columns: new[] { "ThemeId", "Count", "Trigger" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemThemeEffects");

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 19L, (byte)8 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 20L, (byte)8 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 20L, (byte)30 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 21L, (byte)8 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 21L, (byte)30 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 30L, (byte)1 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 30L, (byte)6 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 31L, (byte)1 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 31L, (byte)6 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 32L, (byte)1 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 32L, (byte)6 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 32L, (byte)15 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 37L, (byte)8 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 38L, (byte)8 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 38L, (byte)30 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 38L, (byte)31 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 39L, (byte)8 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 39L, (byte)30 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 39L, (byte)31 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 40L, (byte)8 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 41L, (byte)5 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 42L, (byte)5 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 42L, (byte)8 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 43L, (byte)5 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 43L, (byte)8 });

            migrationBuilder.DeleteData(
                table: "AuraStatModifiers",
                keyColumns: new[] { "AuraId", "Stat" },
                keyValues: new object[] { 44L, (byte)31 });

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 16L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 17L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 18L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 22L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 23L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 24L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 25L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 26L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 27L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 28L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 29L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 33L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 34L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 35L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 36L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 19L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 20L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 21L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 30L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 31L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 32L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 37L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 38L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 39L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 40L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 41L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 42L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 43L);

            migrationBuilder.DeleteData(
                table: "AuraTemplates",
                keyColumn: "Id",
                keyValue: 44L);
        }
    }
}
