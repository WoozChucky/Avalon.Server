using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class ClassNeutralAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassStatFactors_BaseCrit",
                table: "ClassStatFactors");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassStatFactors_HpPerStamina",
                table: "ClassStatFactors");

            migrationBuilder.DropColumn(
                name: "BaseCrit",
                table: "ClassStatFactors");

            migrationBuilder.DropColumn(
                name: "HpPerStamina",
                table: "ClassStatFactors");

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 1 },
                column: "BaseHp",
                value: 130L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 2 },
                column: "BaseHp",
                value: 160L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 3 },
                column: "BaseHp",
                value: 190L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 4 },
                column: "BaseHp",
                value: 220L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 5 },
                column: "BaseHp",
                value: 250L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 6 },
                column: "BaseHp",
                value: 280L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 7 },
                column: "BaseHp",
                value: 310L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 8 },
                column: "BaseHp",
                value: 340L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 9 },
                column: "BaseHp",
                value: 370L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 10 },
                column: "BaseHp",
                value: 400L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 11 },
                column: "BaseHp",
                value: 430L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 12 },
                column: "BaseHp",
                value: 460L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 13 },
                column: "BaseHp",
                value: 490L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 14 },
                column: "BaseHp",
                value: 520L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 15 },
                column: "BaseHp",
                value: 550L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 16 },
                column: "BaseHp",
                value: 580L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 1 },
                column: "BaseHp",
                value: 78L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 2 },
                column: "BaseHp",
                value: 99L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 3 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 120L, 24L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 4 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 141L, 25L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 5 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 162L, 27L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 6 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 183L, 28L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 7 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 204L, 30L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 8 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 225L, 31L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 9 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 246L, 33L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 10 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 267L, 35L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 11 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 288L, 36L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 12 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 309L, 38L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 13 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 330L, 39L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 14 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 351L, 41L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 15 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 372L, 42L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 16 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 393L, 44L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 1 },
                column: "BaseHp",
                value: 58L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 2 },
                column: "BaseHp",
                value: 78L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 3 },
                column: "BaseHp",
                value: 98L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 4 },
                column: "BaseHp",
                value: 118L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 5 },
                column: "BaseHp",
                value: 138L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 6 },
                column: "BaseHp",
                value: 158L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 7 },
                column: "BaseHp",
                value: 178L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 8 },
                column: "BaseHp",
                value: 198L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 9 },
                column: "BaseHp",
                value: 218L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 10 },
                column: "BaseHp",
                value: 238L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 11 },
                column: "BaseHp",
                value: 258L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 12 },
                column: "BaseHp",
                value: 278L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 13 },
                column: "BaseHp",
                value: 298L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 14 },
                column: "BaseHp",
                value: 318L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 15 },
                column: "BaseHp",
                value: 338L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 16 },
                column: "BaseHp",
                value: 358L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "BaseCrit",
                table: "ClassStatFactors",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<long>(
                name: "HpPerStamina",
                table: "ClassStatFactors",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 1 },
                column: "BaseHp",
                value: 20L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 2 },
                column: "BaseHp",
                value: 40L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 3 },
                column: "BaseHp",
                value: 60L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 4 },
                column: "BaseHp",
                value: 80L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 5 },
                column: "BaseHp",
                value: 100L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 6 },
                column: "BaseHp",
                value: 120L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 7 },
                column: "BaseHp",
                value: 140L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 8 },
                column: "BaseHp",
                value: 160L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 9 },
                column: "BaseHp",
                value: 180L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 10 },
                column: "BaseHp",
                value: 200L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 11 },
                column: "BaseHp",
                value: 220L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 12 },
                column: "BaseHp",
                value: 240L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 13 },
                column: "BaseHp",
                value: 260L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 14 },
                column: "BaseHp",
                value: 280L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 15 },
                column: "BaseHp",
                value: 300L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 16 },
                column: "BaseHp",
                value: 320L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 1 },
                column: "BaseHp",
                value: 18L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 2 },
                column: "BaseHp",
                value: 36L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 3 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 54L, 23L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 4 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 72L, 24L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 5 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 90L, 25L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 6 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 108L, 26L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 7 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 126L, 27L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 8 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 144L, 28L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 9 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 162L, 29L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 10 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 180L, 30L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 11 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 198L, 31L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 12 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 216L, 32L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 13 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 234L, 33L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 14 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 252L, 34L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 15 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 270L, 35L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 16 },
                columns: new[] { "BaseHp", "Strength" },
                values: new object[] { 288L, 36L });

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 1 },
                column: "BaseHp",
                value: 18L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 2 },
                column: "BaseHp",
                value: 36L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 3 },
                column: "BaseHp",
                value: 54L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 4 },
                column: "BaseHp",
                value: 72L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 5 },
                column: "BaseHp",
                value: 90L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 6 },
                column: "BaseHp",
                value: 108L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 7 },
                column: "BaseHp",
                value: 126L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 8 },
                column: "BaseHp",
                value: 144L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 9 },
                column: "BaseHp",
                value: 162L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 10 },
                column: "BaseHp",
                value: 180L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 11 },
                column: "BaseHp",
                value: 198L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 12 },
                column: "BaseHp",
                value: 216L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 13 },
                column: "BaseHp",
                value: 234L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 14 },
                column: "BaseHp",
                value: 252L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 15 },
                column: "BaseHp",
                value: 270L);

            migrationBuilder.UpdateData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 16 },
                column: "BaseHp",
                value: 288L);

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)1,
                columns: new[] { "BaseCrit", "HpPerStamina" },
                values: new object[] { 5f, 10L });

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)2,
                columns: new[] { "BaseCrit", "HpPerStamina" },
                values: new object[] { 1.85f, 5L });

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)3,
                columns: new[] { "BaseCrit", "HpPerStamina" },
                values: new object[] { 5f, 8L });

            migrationBuilder.UpdateData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)4,
                columns: new[] { "BaseCrit", "HpPerStamina" },
                values: new object[] { 1.85f, 7L });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassStatFactors_BaseCrit",
                table: "ClassStatFactors",
                sql: "\"BaseCrit\" >= 0 AND \"BaseCrit\" < 'Infinity'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassStatFactors_HpPerStamina",
                table: "ClassStatFactors",
                sql: "\"HpPerStamina\" >= 0 AND \"HpPerStamina\" <= 4294967295");
        }
    }
}
