using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class NarrowCharacterClassToSmallint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 1 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 2 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 3 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 4 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 1, 5 });

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
                keyValues: new object[] { 2, 1 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 2 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 3 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 4 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 2, 5 });

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
                keyValues: new object[] { 3, 1 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 2 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 3 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 4 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 3, 5 });

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
                keyValues: new object[] { 4, 1 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 2 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 3 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 4 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { 4, 5 });

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

            migrationBuilder.DeleteData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: 4);

            migrationBuilder.AlterColumn<byte>(
                name: "ClassRequirement",
                table: "QuestTemplates",
                type: "smallint",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte>(
                name: "Class",
                table: "ClassStatFactors",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<byte>(
                name: "Class",
                table: "ClassLevelStats",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<byte>(
                name: "Class",
                table: "CharacterCreateInfos",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<byte[]>(
                name: "AllowedClasses",
                table: "AbilityTemplates",
                type: "smallint[]",
                nullable: false,
                oldClrType: typeof(int[]),
                oldType: "integer[]");

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                column: "AllowedClasses",
                value: new byte[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 201L,
                column: "AllowedClasses",
                value: new byte[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 202L,
                column: "AllowedClasses",
                value: new byte[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 203L,
                column: "AllowedClasses",
                value: new byte[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                column: "AllowedClasses",
                value: new byte[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 211L,
                column: "AllowedClasses",
                value: new byte[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 212L,
                column: "AllowedClasses",
                value: new byte[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 213L,
                column: "AllowedClasses",
                value: new byte[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                column: "AllowedClasses",
                value: new byte[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 221L,
                column: "AllowedClasses",
                value: new byte[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 222L,
                column: "AllowedClasses",
                value: new byte[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 223L,
                column: "AllowedClasses",
                value: new byte[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                column: "AllowedClasses",
                value: new byte[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 231L,
                column: "AllowedClasses",
                value: new byte[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 232L,
                column: "AllowedClasses",
                value: new byte[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 233L,
                column: "AllowedClasses",
                value: new byte[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 234L,
                column: "AllowedClasses",
                value: new byte[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 300L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 301L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 302L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 303L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 304L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 305L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 306L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 307L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 308L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 309L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 310L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 311L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 312L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 313L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 314L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 315L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 316L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 317L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 318L,
                column: "AllowedClasses",
                value: new byte[0]);

            migrationBuilder.InsertData(
                table: "CharacterCreateInfos",
                columns: new[] { "Class", "Map", "Rotation", "StartingItems", "StartingSpells", "X", "Y", "Z" },
                values: new object[,]
                {
                    { (byte)1, 1, 0f, "1,2,3", "200,201,202,203", 25f, 51f, 25f },
                    { (byte)2, 1, 0f, "1,2", "210,211,212,213", 25f, 51f, 25f },
                    { (byte)3, 1, 0f, "1,2", "220,221,222,223", 25f, 51f, 25f },
                    { (byte)4, 1, 0f, "1,2", "230,231,232,233,234", 25f, 51f, 25f }
                });

            migrationBuilder.InsertData(
                table: "ClassLevelStats",
                columns: new[] { "Class", "Level", "Agility", "BaseHp", "BaseMana", "Intellect", "Stamina", "Strength" },
                values: new object[,]
                {
                    { (byte)1, 1, 20L, 20L, 0L, 20L, 22L, 23L },
                    { (byte)1, 2, 21L, 40L, 0L, 20L, 24L, 25L },
                    { (byte)1, 3, 23L, 60L, 0L, 20L, 26L, 27L },
                    { (byte)1, 4, 24L, 80L, 0L, 20L, 28L, 29L },
                    { (byte)1, 5, 26L, 100L, 0L, 20L, 30L, 31L },
                    { (byte)1, 6, 27L, 120L, 0L, 20L, 32L, 33L },
                    { (byte)1, 7, 29L, 140L, 0L, 20L, 34L, 35L },
                    { (byte)1, 8, 30L, 160L, 0L, 20L, 36L, 37L },
                    { (byte)1, 9, 32L, 180L, 0L, 20L, 38L, 39L },
                    { (byte)1, 10, 33L, 200L, 0L, 20L, 40L, 41L },
                    { (byte)1, 11, 35L, 220L, 0L, 20L, 42L, 43L },
                    { (byte)1, 12, 36L, 240L, 0L, 20L, 44L, 45L },
                    { (byte)1, 13, 38L, 260L, 0L, 20L, 46L, 47L },
                    { (byte)1, 14, 39L, 280L, 0L, 20L, 48L, 49L },
                    { (byte)1, 15, 41L, 300L, 0L, 20L, 50L, 51L },
                    { (byte)1, 16, 42L, 320L, 0L, 20L, 52L, 53L },
                    { (byte)2, 1, 20L, 16L, 20L, 23L, 21L, 20L },
                    { (byte)2, 2, 21L, 32L, 40L, 25L, 22L, 20L },
                    { (byte)2, 3, 22L, 48L, 60L, 27L, 23L, 20L },
                    { (byte)2, 4, 23L, 64L, 80L, 29L, 24L, 20L },
                    { (byte)2, 5, 24L, 80L, 100L, 31L, 25L, 20L },
                    { (byte)2, 6, 25L, 96L, 120L, 33L, 26L, 20L },
                    { (byte)2, 7, 26L, 112L, 140L, 35L, 27L, 20L },
                    { (byte)2, 8, 27L, 128L, 160L, 37L, 28L, 20L },
                    { (byte)2, 9, 28L, 144L, 180L, 39L, 29L, 20L },
                    { (byte)2, 10, 29L, 160L, 200L, 41L, 30L, 20L },
                    { (byte)2, 11, 30L, 176L, 220L, 43L, 31L, 20L },
                    { (byte)2, 12, 31L, 192L, 240L, 45L, 32L, 20L },
                    { (byte)2, 13, 32L, 208L, 260L, 47L, 33L, 20L },
                    { (byte)2, 14, 33L, 224L, 280L, 49L, 34L, 20L },
                    { (byte)2, 15, 34L, 240L, 300L, 51L, 35L, 20L },
                    { (byte)2, 16, 35L, 256L, 320L, 53L, 36L, 20L },
                    { (byte)3, 1, 23L, 18L, 10L, 20L, 20L, 21L },
                    { (byte)3, 2, 25L, 36L, 20L, 20L, 21L, 22L },
                    { (byte)3, 3, 27L, 54L, 30L, 20L, 22L, 23L },
                    { (byte)3, 4, 29L, 72L, 40L, 20L, 23L, 24L },
                    { (byte)3, 5, 31L, 90L, 50L, 20L, 24L, 25L },
                    { (byte)3, 6, 33L, 108L, 60L, 20L, 25L, 26L },
                    { (byte)3, 7, 35L, 126L, 70L, 20L, 26L, 27L },
                    { (byte)3, 8, 37L, 144L, 80L, 20L, 27L, 28L },
                    { (byte)3, 9, 39L, 162L, 90L, 20L, 28L, 29L },
                    { (byte)3, 10, 41L, 180L, 100L, 20L, 29L, 30L },
                    { (byte)3, 11, 43L, 198L, 110L, 20L, 30L, 31L },
                    { (byte)3, 12, 45L, 216L, 120L, 20L, 31L, 32L },
                    { (byte)3, 13, 47L, 234L, 130L, 20L, 32L, 33L },
                    { (byte)3, 14, 49L, 252L, 140L, 20L, 33L, 34L },
                    { (byte)3, 15, 51L, 270L, 150L, 20L, 34L, 35L },
                    { (byte)3, 16, 53L, 288L, 160L, 20L, 35L, 36L },
                    { (byte)4, 1, 21L, 18L, 20L, 23L, 20L, 20L },
                    { (byte)4, 2, 22L, 36L, 40L, 25L, 21L, 20L },
                    { (byte)4, 3, 23L, 54L, 60L, 27L, 22L, 20L },
                    { (byte)4, 4, 24L, 72L, 80L, 29L, 23L, 20L },
                    { (byte)4, 5, 25L, 90L, 100L, 31L, 24L, 20L },
                    { (byte)4, 6, 26L, 108L, 120L, 33L, 25L, 20L },
                    { (byte)4, 7, 27L, 126L, 140L, 35L, 26L, 20L },
                    { (byte)4, 8, 28L, 144L, 160L, 37L, 27L, 20L },
                    { (byte)4, 9, 29L, 162L, 180L, 39L, 28L, 20L },
                    { (byte)4, 10, 30L, 180L, 200L, 41L, 29L, 20L },
                    { (byte)4, 11, 31L, 198L, 220L, 43L, 30L, 20L },
                    { (byte)4, 12, 32L, 216L, 240L, 45L, 31L, 20L },
                    { (byte)4, 13, 33L, 234L, 260L, 47L, 32L, 20L },
                    { (byte)4, 14, 34L, 252L, 280L, 49L, 33L, 20L },
                    { (byte)4, 15, 35L, 270L, 300L, 51L, 34L, 20L },
                    { (byte)4, 16, 36L, 288L, 320L, 53L, 35L, 20L }
                });

            migrationBuilder.InsertData(
                table: "ClassStatFactors",
                columns: new[] { "Class", "AbilityPerIntellect", "AttackPerAgility", "AttackPerStrength", "BaseBlock", "BaseCrit", "BaseDodge", "FixedPower", "HpPerStamina", "PowerPerAgility", "PowerPerIntellect" },
                values: new object[,]
                {
                    { (byte)1, 0.20000000000000001, 0.0, 2.0, 5f, 5f, 3.664f, 100L, 10L, 0.0, 0.0 },
                    { (byte)2, 3.0, 0.0, 0.5, 0f, 1.85f, 3.25f, null, 5L, 0.0, 15.0 },
                    { (byte)3, 0.5, 1.5, 0.5, 0f, 5f, 4.35f, null, 8L, 0.80000000000000004, 2.0 },
                    { (byte)4, 2.0, 0.0, 0.5, 0f, 1.85f, 3.25f, null, 7L, 0.0, 12.0 }
                });

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 1L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 2L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 3L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 4L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 5L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 6L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 7L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 8L,
                column: "ClassRequirement",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)1);

            migrationBuilder.DeleteData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)2);

            migrationBuilder.DeleteData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)3);

            migrationBuilder.DeleteData(
                table: "CharacterCreateInfos",
                keyColumn: "Class",
                keyValue: (byte)4);

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 1 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 2 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 3 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 4 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 5 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 6 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 7 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 8 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 9 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 10 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 11 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 12 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 13 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 14 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 15 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)1, 16 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 1 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 2 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 3 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 4 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 5 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 6 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 7 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 8 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 9 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 10 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 11 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 12 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 13 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 14 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 15 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)2, 16 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 1 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 2 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 3 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 4 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 5 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 6 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 7 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 8 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 9 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 10 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 11 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 12 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 13 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 14 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 15 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)3, 16 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 1 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 2 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 3 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 4 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 5 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 6 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 7 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 8 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 9 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 10 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 11 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 12 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 13 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 14 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 15 });

            migrationBuilder.DeleteData(
                table: "ClassLevelStats",
                keyColumns: new[] { "Class", "Level" },
                keyValues: new object[] { (byte)4, 16 });

            migrationBuilder.DeleteData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)1);

            migrationBuilder.DeleteData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)2);

            migrationBuilder.DeleteData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)3);

            migrationBuilder.DeleteData(
                table: "ClassStatFactors",
                keyColumn: "Class",
                keyValue: (byte)4);

            migrationBuilder.AlterColumn<int>(
                name: "ClassRequirement",
                table: "QuestTemplates",
                type: "integer",
                nullable: true,
                oldClrType: typeof(byte),
                oldType: "smallint",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Class",
                table: "ClassStatFactors",
                type: "integer",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<int>(
                name: "Class",
                table: "ClassLevelStats",
                type: "integer",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<int>(
                name: "Class",
                table: "CharacterCreateInfos",
                type: "integer",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<int[]>(
                name: "AllowedClasses",
                table: "AbilityTemplates",
                type: "integer[]",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "smallint[]");

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 200L,
                column: "AllowedClasses",
                value: new[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 201L,
                column: "AllowedClasses",
                value: new[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 202L,
                column: "AllowedClasses",
                value: new[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 203L,
                column: "AllowedClasses",
                value: new[] { 1 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 210L,
                column: "AllowedClasses",
                value: new[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 211L,
                column: "AllowedClasses",
                value: new[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 212L,
                column: "AllowedClasses",
                value: new[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 213L,
                column: "AllowedClasses",
                value: new[] { 2 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 220L,
                column: "AllowedClasses",
                value: new[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 221L,
                column: "AllowedClasses",
                value: new[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 222L,
                column: "AllowedClasses",
                value: new[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 223L,
                column: "AllowedClasses",
                value: new[] { 3 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 230L,
                column: "AllowedClasses",
                value: new[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 231L,
                column: "AllowedClasses",
                value: new[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 232L,
                column: "AllowedClasses",
                value: new[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 233L,
                column: "AllowedClasses",
                value: new[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 234L,
                column: "AllowedClasses",
                value: new[] { 4 });

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 300L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 301L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 302L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 303L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 304L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 305L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 306L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 307L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 308L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 309L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 310L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 311L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 312L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 313L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 314L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 315L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 316L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 317L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.UpdateData(
                table: "AbilityTemplates",
                keyColumn: "Id",
                keyValue: 318L,
                column: "AllowedClasses",
                value: new int[0]);

            migrationBuilder.InsertData(
                table: "CharacterCreateInfos",
                columns: new[] { "Class", "Map", "Rotation", "StartingItems", "StartingSpells", "X", "Y", "Z" },
                values: new object[,]
                {
                    { 1, 1, 0f, "1,2,3", "200,201,202,203", 25f, 51f, 25f },
                    { 2, 1, 0f, "1,2", "210,211,212,213", 25f, 51f, 25f },
                    { 3, 1, 0f, "1,2", "220,221,222,223", 25f, 51f, 25f },
                    { 4, 1, 0f, "1,2", "230,231,232,233,234", 25f, 51f, 25f }
                });

            migrationBuilder.InsertData(
                table: "ClassLevelStats",
                columns: new[] { "Class", "Level", "Agility", "BaseHp", "BaseMana", "Intellect", "Stamina", "Strength" },
                values: new object[,]
                {
                    { 1, 1, 20L, 20L, 0L, 20L, 22L, 23L },
                    { 1, 2, 21L, 40L, 0L, 20L, 24L, 25L },
                    { 1, 3, 23L, 60L, 0L, 20L, 26L, 27L },
                    { 1, 4, 24L, 80L, 0L, 20L, 28L, 29L },
                    { 1, 5, 26L, 100L, 0L, 20L, 30L, 31L },
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
                    { 2, 1, 20L, 16L, 20L, 23L, 21L, 20L },
                    { 2, 2, 21L, 32L, 40L, 25L, 22L, 20L },
                    { 2, 3, 22L, 48L, 60L, 27L, 23L, 20L },
                    { 2, 4, 23L, 64L, 80L, 29L, 24L, 20L },
                    { 2, 5, 24L, 80L, 100L, 31L, 25L, 20L },
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
                    { 3, 1, 23L, 18L, 10L, 20L, 20L, 21L },
                    { 3, 2, 25L, 36L, 20L, 20L, 21L, 22L },
                    { 3, 3, 27L, 54L, 30L, 20L, 22L, 23L },
                    { 3, 4, 29L, 72L, 40L, 20L, 23L, 24L },
                    { 3, 5, 31L, 90L, 50L, 20L, 24L, 25L },
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
                    { 4, 1, 21L, 18L, 20L, 23L, 20L, 20L },
                    { 4, 2, 22L, 36L, 40L, 25L, 21L, 20L },
                    { 4, 3, 23L, 54L, 60L, 27L, 22L, 20L },
                    { 4, 4, 24L, 72L, 80L, 29L, 23L, 20L },
                    { 4, 5, 25L, 90L, 100L, 31L, 24L, 20L },
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

            migrationBuilder.InsertData(
                table: "ClassStatFactors",
                columns: new[] { "Class", "AbilityPerIntellect", "AttackPerAgility", "AttackPerStrength", "BaseBlock", "BaseCrit", "BaseDodge", "FixedPower", "HpPerStamina", "PowerPerAgility", "PowerPerIntellect" },
                values: new object[,]
                {
                    { 1, 0.20000000000000001, 0.0, 2.0, 5f, 5f, 3.664f, 100L, 10L, 0.0, 0.0 },
                    { 2, 3.0, 0.0, 0.5, 0f, 1.85f, 3.25f, null, 5L, 0.0, 15.0 },
                    { 3, 0.5, 1.5, 0.5, 0f, 5f, 4.35f, null, 8L, 0.80000000000000004, 2.0 },
                    { 4, 2.0, 0.0, 0.5, 0f, 1.85f, 3.25f, null, 7L, 0.0, 12.0 }
                });

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 1L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 2L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 3L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 4L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 5L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 6L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 7L,
                column: "ClassRequirement",
                value: null);

            migrationBuilder.UpdateData(
                table: "QuestTemplates",
                keyColumn: "Id",
                keyValue: 8L,
                column: "ClassRequirement",
                value: null);
        }
    }
}
