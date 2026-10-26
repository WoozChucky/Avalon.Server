using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class ManaFormula : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassStatFactors");

            migrationBuilder.DropColumn(
                name: "BaseMana",
                table: "ClassLevelStats");

            migrationBuilder.AddColumn<long>(
                name: "ManaBase",
                table: "CombatFormula",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<float>(
                name: "ManaPerIntellect",
                table: "CombatFormula",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<long>(
                name: "ManaPerLevel",
                table: "CombatFormula",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.UpdateData(
                table: "CombatFormula",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ManaBase", "ManaPerIntellect", "ManaPerLevel" },
                values: new object[] { 100L, 4f, 8L });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_ManaBase",
                table: "CombatFormula",
                sql: "\"ManaBase\" >= 0 AND \"ManaBase\" <= 4294967295");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_ManaPerIntellect",
                table: "CombatFormula",
                sql: "\"ManaPerIntellect\" >= 0 AND \"ManaPerIntellect\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CombatFormula_ManaPerLevel",
                table: "CombatFormula",
                sql: "\"ManaPerLevel\" >= 0 AND \"ManaPerLevel\" <= 4294967295");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_ManaBase",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_ManaPerIntellect",
                table: "CombatFormula");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CombatFormula_ManaPerLevel",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "ManaBase",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "ManaPerIntellect",
                table: "CombatFormula");

            migrationBuilder.DropColumn(
                name: "ManaPerLevel",
                table: "CombatFormula");

            migrationBuilder.AddColumn<long>(
                name: "BaseMana",
                table: "ClassLevelStats",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ClassStatFactors",
                columns: table => new
                {
                    Class = table.Column<byte>(type: "smallint", nullable: false),
                    FixedPower = table.Column<long>(type: "bigint", nullable: true),
                    PowerPerAgility = table.Column<double>(type: "double precision", nullable: false),
                    PowerPerIntellect = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassStatFactors", x => x.Class);
                    table.CheckConstraint("CK_ClassStatFactors_FixedPower", "\"FixedPower\" IS NULL OR (\"FixedPower\" >= 0 AND \"FixedPower\" <= 4294967295)");
                    table.CheckConstraint("CK_ClassStatFactors_PowerPerAgility", "\"PowerPerAgility\" >= 0 AND \"PowerPerAgility\" < 'Infinity'");
                    table.CheckConstraint("CK_ClassStatFactors_PowerPerIntellect", "\"PowerPerIntellect\" >= 0 AND \"PowerPerIntellect\" < 'Infinity'");
                });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 1 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 2 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 3 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 4 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 5 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 6 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 7 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 8 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 9 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 10 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 11 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 12 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 13 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 14 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 15 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 16 },
                column: "BaseMana",
                value: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 1 },
                column: "BaseMana",
                value: 20L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 2 },
                column: "BaseMana",
                value: 40L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 3 },
                column: "BaseMana",
                value: 60L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 4 },
                column: "BaseMana",
                value: 80L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 5 },
                column: "BaseMana",
                value: 100L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 6 },
                column: "BaseMana",
                value: 120L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 7 },
                column: "BaseMana",
                value: 140L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 8 },
                column: "BaseMana",
                value: 160L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 9 },
                column: "BaseMana",
                value: 180L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 10 },
                column: "BaseMana",
                value: 200L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 11 },
                column: "BaseMana",
                value: 220L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 12 },
                column: "BaseMana",
                value: 240L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 13 },
                column: "BaseMana",
                value: 260L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 14 },
                column: "BaseMana",
                value: 280L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 15 },
                column: "BaseMana",
                value: 300L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 16 },
                column: "BaseMana",
                value: 320L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 1 },
                column: "BaseMana",
                value: 10L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 2 },
                column: "BaseMana",
                value: 20L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 3 },
                column: "BaseMana",
                value: 30L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 4 },
                column: "BaseMana",
                value: 40L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 5 },
                column: "BaseMana",
                value: 50L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 6 },
                column: "BaseMana",
                value: 60L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 7 },
                column: "BaseMana",
                value: 70L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 8 },
                column: "BaseMana",
                value: 80L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 9 },
                column: "BaseMana",
                value: 90L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 10 },
                column: "BaseMana",
                value: 100L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 11 },
                column: "BaseMana",
                value: 110L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 12 },
                column: "BaseMana",
                value: 120L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 13 },
                column: "BaseMana",
                value: 130L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 14 },
                column: "BaseMana",
                value: 140L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 15 },
                column: "BaseMana",
                value: 150L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 16 },
                column: "BaseMana",
                value: 160L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 1 },
                column: "BaseMana",
                value: 20L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 2 },
                column: "BaseMana",
                value: 40L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 3 },
                column: "BaseMana",
                value: 60L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 4 },
                column: "BaseMana",
                value: 80L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 5 },
                column: "BaseMana",
                value: 100L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 6 },
                column: "BaseMana",
                value: 120L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 7 },
                column: "BaseMana",
                value: 140L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 8 },
                column: "BaseMana",
                value: 160L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 9 },
                column: "BaseMana",
                value: 180L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 10 },
                column: "BaseMana",
                value: 200L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 11 },
                column: "BaseMana",
                value: 220L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 12 },
                column: "BaseMana",
                value: 240L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 13 },
                column: "BaseMana",
                value: 260L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 14 },
                column: "BaseMana",
                value: 280L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 15 },
                column: "BaseMana",
                value: 300L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 16 },
                column: "BaseMana",
                value: 320L);

            migrationBuilder.InsertData(
                table: "ClassStatFactors",
                columns: new[] { "Class", "FixedPower", "PowerPerAgility", "PowerPerIntellect" },
                values: new object[,]
                {
                    { (byte)1, 100L, 0.0, 0.0 },
                    { (byte)2, null, 0.0, 15.0 },
                    { (byte)3, null, 0.80000000000000004, 2.0 },
                    { (byte)4, null, 0.0, 12.0 }
                });
        }
    }
}
