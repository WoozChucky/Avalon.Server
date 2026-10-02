using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddAuras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AuraId",
                table: "AbilityTemplates",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuraTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    TickIntervalMs = table.Column<long>(type: "bigint", nullable: false),
                    PeriodicKind = table.Column<byte>(type: "smallint", nullable: false),
                    PeriodicBase = table.Column<float>(type: "real", nullable: false),
                    ScalingStat = table.Column<byte>(type: "smallint", nullable: false),
                    ScalingCoefficient = table.Column<float>(type: "real", nullable: false),
                    BaseDamageCoefficient = table.Column<float>(type: "real", nullable: false),
                    Stacking = table.Column<byte>(type: "smallint", nullable: false),
                    MaxStacks = table.Column<long>(type: "bigint", nullable: false),
                    ScriptName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuraTemplates", x => x.Id);
                    table.CheckConstraint("CK_AuraTemplates_BaseDamageCoefficient", "\"BaseDamageCoefficient\" >= 0 AND \"BaseDamageCoefficient\" < 'Infinity'");
                    table.CheckConstraint("CK_AuraTemplates_DurationMs", "\"DurationMs\" > 0");
                    table.CheckConstraint("CK_AuraTemplates_Kind", "\"Kind\" IN (1, 2)");
                    table.CheckConstraint("CK_AuraTemplates_MaxStacks", "\"MaxStacks\" >= 1");
                    table.CheckConstraint("CK_AuraTemplates_PeriodicBase", "\"PeriodicBase\" >= 0 AND \"PeriodicBase\" < 'Infinity'");
                    table.CheckConstraint("CK_AuraTemplates_PeriodicFitsKind", "(\"Kind\" = 2 OR \"PeriodicKind\" <> 1) AND (\"Kind\" = 1 OR \"PeriodicKind\" <> 2)");
                    table.CheckConstraint("CK_AuraTemplates_PeriodicKind", "\"PeriodicKind\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_AuraTemplates_ScalingCoefficient", "\"ScalingCoefficient\" >= 0 AND \"ScalingCoefficient\" < 'Infinity'");
                    table.CheckConstraint("CK_AuraTemplates_ScalingStat", "\"ScalingStat\" IN (0, 1)");
                    table.CheckConstraint("CK_AuraTemplates_Stacking", "\"Stacking\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_AuraTemplates_TickIntervalMs", "\"TickIntervalMs\" <= \"DurationMs\" AND (\"PeriodicKind\" = 0 OR \"TickIntervalMs\" > 0)");
                });

            migrationBuilder.CreateTable(
                name: "AuraStatModifiers",
                columns: table => new
                {
                    AuraId = table.Column<long>(type: "bigint", nullable: false),
                    Stat = table.Column<byte>(type: "smallint", nullable: false),
                    Value = table.Column<float>(type: "real", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuraStatModifiers", x => new { x.AuraId, x.Stat });
                    table.CheckConstraint("CK_AuraStatModifiers_Kind", "\"Kind\" IN (1, 2)");
                    table.CheckConstraint("CK_AuraStatModifiers_Stat", "\"Stat\" BETWEEN 1 AND 10");
                    table.CheckConstraint("CK_AuraStatModifiers_Value", "\"Value\" > -1000000 AND \"Value\" < 'Infinity' AND (\"Kind\" = 1 OR \"Value\" > -100)");
                    table.ForeignKey(
                        name: "FK_AuraStatModifiers_AuraTemplates_AuraId",
                        column: x => x.AuraId,
                        principalTable: "AuraTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 201L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 202L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 211L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 212L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 221L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 222L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 231L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 232L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 300L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 301L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 302L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 303L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 304L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 305L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 306L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 307L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 308L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 309L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 310L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 311L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 312L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 313L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 314L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 315L,
                column: "AuraId",
                value: null);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 316L,
                column: "AuraId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_AbilityTemplates_AuraId",
                table: "AbilityTemplates",
                column: "AuraId");

            migrationBuilder.AddForeignKey(
                name: "FK_AbilityTemplates_AuraTemplates_AuraId",
                table: "AbilityTemplates",
                column: "AuraId",
                principalTable: "AuraTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AbilityTemplates_AuraTemplates_AuraId",
                table: "AbilityTemplates");

            migrationBuilder.DropTable(
                name: "AuraStatModifiers");

            migrationBuilder.DropTable(
                name: "AuraTemplates");

            migrationBuilder.DropIndex(
                name: "IX_AbilityTemplates_AuraId",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "AuraId",
                table: "AbilityTemplates");
        }
    }
}
