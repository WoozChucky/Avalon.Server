using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatureBaseStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreatureBaseStats",
                columns: table => new
                {
                    Level = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Health = table.Column<long>(type: "bigint", nullable: false),
                    DamageMin = table.Column<long>(type: "bigint", nullable: false),
                    DamageMax = table.Column<long>(type: "bigint", nullable: false),
                    Experience = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatureBaseStats", x => x.Level);
                });

            migrationBuilder.CreateTable(
                name: "CreatureRarityModifiers",
                columns: table => new
                {
                    Rarity = table.Column<int>(type: "integer", nullable: false),
                    HealthMultiplier = table.Column<float>(type: "real", nullable: false),
                    DamageMultiplier = table.Column<float>(type: "real", nullable: false),
                    ExperienceMultiplier = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatureRarityModifiers", x => x.Rarity);
                });

            migrationBuilder.InsertData(
                table: "CreatureBaseStats",
                columns: new[] { "Level", "DamageMax", "DamageMin", "Experience", "Health" },
                values: new object[,]
                {
                    { 1, 5L, 3L, 15L, 40L },
                    { 2, 7L, 4L, 25L, 52L },
                    { 3, 9L, 5L, 40L, 66L },
                    { 4, 11L, 7L, 60L, 84L },
                    { 5, 14L, 9L, 85L, 106L },
                    { 6, 17L, 11L, 115L, 133L },
                    { 7, 21L, 14L, 150L, 166L },
                    { 8, 26L, 17L, 195L, 206L },
                    { 9, 32L, 21L, 250L, 254L },
                    { 10, 39L, 26L, 320L, 312L }
                });

            migrationBuilder.InsertData(
                table: "CreatureRarityModifiers",
                columns: new[] { "Rarity", "DamageMultiplier", "ExperienceMultiplier", "HealthMultiplier" },
                values: new object[,]
                {
                    { 0, 1f, 1f, 1f },
                    { 1, 1.4f, 3f, 2.5f },
                    { 2, 1.7f, 6f, 4f },
                    { 3, 2.2f, 15f, 8f }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreatureBaseStats");

            migrationBuilder.DropTable(
                name: "CreatureRarityModifiers");
        }
    }
}
