using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class CostsInMana : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_CostPowerType",
                table: "AbilityTemplates");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_PowerGainPerHit_NonNegative",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "CostPowerType",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "PowerGainPerHit",
                table: "AbilityTemplates");

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "UseCooldownGroup",
                value: "mana-potion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CostPowerType",
                table: "AbilityTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PowerGainPerHit",
                table: "AbilityTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 8 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 201L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 2, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 202L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 2, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 203L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 2, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 204L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 2, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 211L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 1, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 212L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 1, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 213L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 1, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 214L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 1, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 221L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 3, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 222L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 3, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 223L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 3, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 224L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 3, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 231L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 1, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 232L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 1, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 233L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 1, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 234L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 1, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 235L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 1, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 300L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 301L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 302L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 303L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 304L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 305L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 306L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 307L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 308L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 309L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 310L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 311L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 312L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 313L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 314L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 315L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 316L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 317L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 318L,
                columns: new[] { "CostPowerType", "PowerGainPerHit" },
                values: new object[] { 0, 0 });

            migrationBuilder.UpdateData(
                table: "ItemTemplates",
                keyColumn: "Id",
                keyValue: 2m,
                column: "UseCooldownGroup",
                value: "potion");

            // A row a live template edit gave a cost while it was free is still at pool 0 here, which the
            // re-added check refuses: put it on Mana. Rows just restored to Fury or Energy keep their pool.
            migrationBuilder.Sql(
                "UPDATE \"AbilityTemplates\" SET \"CostPowerType\" = 1 WHERE \"Cost\" > 0 AND \"CostPowerType\" = 0;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_CostPowerType",
                table: "AbilityTemplates",
                sql: "\"CostPowerType\" BETWEEN 0 AND 3 AND (\"Cost\" = 0 OR \"CostPowerType\" <> 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_PowerGainPerHit_NonNegative",
                table: "AbilityTemplates",
                sql: "\"PowerGainPerHit\" >= 0");
        }
    }
}
