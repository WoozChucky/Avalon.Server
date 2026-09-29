using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddAbilityCostPowerType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CostPowerType",
                table: "AbilityTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 201L,
                column: "CostPowerType",
                value: 2);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 202L,
                column: "CostPowerType",
                value: 2);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 211L,
                column: "CostPowerType",
                value: 1);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 212L,
                column: "CostPowerType",
                value: 1);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 221L,
                column: "CostPowerType",
                value: 3);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 222L,
                column: "CostPowerType",
                value: 3);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 231L,
                column: "CostPowerType",
                value: 1);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 232L,
                column: "CostPowerType",
                value: 1);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 300L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 301L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 302L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 303L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 304L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 305L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 306L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 307L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 308L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 309L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 310L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 311L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 312L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 313L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 314L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 315L,
                column: "CostPowerType",
                value: 0);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 316L,
                column: "CostPowerType",
                value: 0);

            // Rows the seed does not own (added by hand, for a test say): a cost is spent from the pool of the
            // classes the row allows, when they all share one (Warrior 1 Fury 2; Wizard 2 and Healer 4 Mana 1;
            // Hunter 3 Energy 3). Never the pool of whoever the ability is later given to.
            migrationBuilder.Sql("""
                UPDATE "AbilityTemplates"
                SET "CostPowerType" = CASE
                    WHEN "AllowedClasses" <@ ARRAY[1] THEN 2
                    WHEN "AllowedClasses" <@ ARRAY[2, 4] THEN 1
                    WHEN "AllowedClasses" <@ ARRAY[3] THEN 3
                END
                WHERE "Cost" > 0 AND "CostPowerType" = 0 AND cardinality("AllowedClasses") > 0
                  AND ("AllowedClasses" <@ ARRAY[1] OR "AllowedClasses" <@ ARRAY[2, 4] OR "AllowedClasses" <@ ARRAY[3]);
                """);

            // Anything left has a cost and no single pool: its classes spend from different pools, or it allows
            // none. Refuse, naming the rows, rather than guess. The migration runs in one transaction, so the refusal
            // leaves no column behind: the fix is to the row itself (classes that share one pool, a Cost of 0, or no
            // row), and then the migration runs again.
            migrationBuilder.Sql("""
                DO $$
                DECLARE unresolved text;
                BEGIN
                    SELECT string_agg("Id"::text, ', ' ORDER BY "Id") INTO unresolved
                    FROM "AbilityTemplates" WHERE "Cost" > 0 AND "CostPowerType" = 0;
                    IF unresolved IS NOT NULL THEN
                        RAISE EXCEPTION 'AbilityTemplates % have a Cost but allow no classes that share one power pool: give each classes that do (Warrior; Wizard or Healer; Hunter), or a Cost of 0, then migrate again', unresolved;
                    END IF;
                END $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AbilityTemplates_CostPowerType",
                table: "AbilityTemplates",
                sql: "\"CostPowerType\" BETWEEN 0 AND 3 AND (\"Cost\" = 0 OR \"CostPowerType\" <> 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AbilityTemplates_CostPowerType",
                table: "AbilityTemplates");

            migrationBuilder.DropColumn(
                name: "CostPowerType",
                table: "AbilityTemplates");
        }
    }
}
