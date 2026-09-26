using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class SeedClassLevelStatsSixToSixteen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ClassLevelStats",
                columns: new[] { "Class", "Level", "Agility", "BaseHp", "BaseMana", "Intellect", "Stamina", "Strength" },
                values: new object[,]
                {
                    { 1, 6, 27L, 120L, 0L, 20L, 32L, 33L },
                    { 1, 7, 29L, 140L, 0L, 20L, 34L, 35L },
                    { 1, 8, 30L, 160L, 0L, 20L, 36L, 37L },
                    { 1, 9, 32L, 180L, 0L, 20L, 38L, 39L },
                    { 1, 10, 33L, 200L, 0L, 20L, 40L, 41L },
                    { 1, 11, 35L, 220L, 0L, 20L, 42L, 43L },
                    { 1, 12, 36L, 240L, 0L, 20L, 44L, 45L },
                    { 1, 13, 38L, 260L, 0L, 20L, 46L, 47L },
                    { 1, 14, 39L, 280L, 0L, 20L, 48L, 49L },
                    { 1, 15, 41L, 300L, 0L, 20L, 50L, 51L },
                    { 1, 16, 42L, 320L, 0L, 20L, 52L, 53L },
                    { 2, 6, 25L, 96L, 120L, 33L, 26L, 20L },
                    { 2, 7, 26L, 112L, 140L, 35L, 27L, 20L },
                    { 2, 8, 27L, 128L, 160L, 37L, 28L, 20L },
                    { 2, 9, 28L, 144L, 180L, 39L, 29L, 20L },
                    { 2, 10, 29L, 160L, 200L, 41L, 30L, 20L },
                    { 2, 11, 30L, 176L, 220L, 43L, 31L, 20L },
                    { 2, 12, 31L, 192L, 240L, 45L, 32L, 20L },
                    { 2, 13, 32L, 208L, 260L, 47L, 33L, 20L },
                    { 2, 14, 33L, 224L, 280L, 49L, 34L, 20L },
                    { 2, 15, 34L, 240L, 300L, 51L, 35L, 20L },
                    { 2, 16, 35L, 256L, 320L, 53L, 36L, 20L },
                    { 3, 6, 33L, 108L, 60L, 20L, 25L, 26L },
                    { 3, 7, 35L, 126L, 70L, 20L, 26L, 27L },
                    { 3, 8, 37L, 144L, 80L, 20L, 27L, 28L },
                    { 3, 9, 39L, 162L, 90L, 20L, 28L, 29L },
                    { 3, 10, 41L, 180L, 100L, 20L, 29L, 30L },
                    { 3, 11, 43L, 198L, 110L, 20L, 30L, 31L },
                    { 3, 12, 45L, 216L, 120L, 20L, 31L, 32L },
                    { 3, 13, 47L, 234L, 130L, 20L, 32L, 33L },
                    { 3, 14, 49L, 252L, 140L, 20L, 33L, 34L },
                    { 3, 15, 51L, 270L, 150L, 20L, 34L, 35L },
                    { 3, 16, 53L, 288L, 160L, 20L, 35L, 36L },
                    { 4, 6, 26L, 108L, 120L, 33L, 25L, 20L },
                    { 4, 7, 27L, 126L, 140L, 35L, 26L, 20L },
                    { 4, 8, 28L, 144L, 160L, 37L, 27L, 20L },
                    { 4, 9, 29L, 162L, 180L, 39L, 28L, 20L },
                    { 4, 10, 30L, 180L, 200L, 41L, 29L, 20L },
                    { 4, 11, 31L, 198L, 220L, 43L, 30L, 20L },
                    { 4, 12, 32L, 216L, 240L, 45L, 31L, 20L },
                    { 4, 13, 33L, 234L, 260L, 47L, 32L, 20L },
                    { 4, 14, 34L, 252L, 280L, 49L, 33L, 20L },
                    { 4, 15, 35L, 270L, 300L, 51L, 34L, 20L },
                    { 4, 16, 36L, 288L, 320L, 53L, 35L, 20L }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 6 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 7 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 8 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 9 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 10 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 11 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 12 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 13 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 14 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 15 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 16 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 6 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 7 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 8 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 9 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 10 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 11 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 12 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 13 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 14 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 15 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 16 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 6 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 7 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 8 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 9 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 10 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 11 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 12 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 13 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 14 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 15 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 16 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 6 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 7 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 8 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 9 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 10 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 11 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 12 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 13 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 14 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 15 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 16 });
        }
    }
}
