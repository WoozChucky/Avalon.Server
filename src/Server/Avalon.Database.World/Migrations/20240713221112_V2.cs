using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.World.Database.Migrations
{
    /// <inheritdoc />
    public partial class V2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterCreateInfos",
                columns: table => new
                {
                    Class = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Map = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    X = table.Column<float>(type: "float", nullable: false),
                    Y = table.Column<float>(type: "float", nullable: false),
                    Z = table.Column<float>(type: "float", nullable: false),
                    Rotation = table.Column<float>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterCreateInfos", x => x.Class);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CharacterLevelExperiences",
                columns: table => new
                {
                    Level = table.Column<ushort>(type: "smallint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Experience = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterLevelExperiences", x => x.Level);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClassLevelStats",
                columns: table => new
                {
                    Class = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Level = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    BaseHp = table.Column<uint>(type: "int unsigned", nullable: false),
                    BaseMana = table.Column<uint>(type: "int unsigned", nullable: false),
                    Stamina = table.Column<uint>(type: "int unsigned", nullable: false),
                    Strength = table.Column<uint>(type: "int unsigned", nullable: false),
                    Agility = table.Column<uint>(type: "int unsigned", nullable: false),
                    Intellect = table.Column<uint>(type: "int unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassLevelStats", x => new { x.Class, x.Level });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "CharacterCreateInfos",
                columns: new[] { "Class", "Map", "Rotation", "X", "Y", "Z" },
                values: new object[,]
                {
                    { (ushort)1, (ushort)1, 0f, 25f, 51f, 25f },
                    { (ushort)2, (ushort)1, 0f, 25f, 51f, 25f },
                    { (ushort)3, (ushort)1, 0f, 25f, 51f, 25f },
                    { (ushort)4, (ushort)1, 0f, 25f, 51f, 25f }
                });

            migrationBuilder.InsertData(
                table: "CharacterLevelExperiences",
                columns: new[] { "Level", "Experience" },
                values: new object[,]
                {
                    { (ushort)1, 400ul },
                    { (ushort)2, 900ul },
                    { (ushort)3, 1400ul },
                    { (ushort)4, 2100ul },
                    { (ushort)5, 2800ul },
                    { (ushort)6, 3600ul },
                    { (ushort)7, 4500ul },
                    { (ushort)8, 5400ul },
                    { (ushort)9, 6500ul },
                    { (ushort)10, 7600ul },
                    { (ushort)11, 8700ul },
                    { (ushort)12, 9800ul },
                    { (ushort)13, 11000ul },
                    { (ushort)14, 12300ul },
                    { (ushort)15, 13600ul }
                });

            migrationBuilder.InsertData(
                table: "ClassLevelStats",
                columns: new[] { "Class", "Level", "Agility", "BaseHp", "BaseMana", "Intellect", "Stamina", "Strength" },
                values: new object[,]
                {
                    { (ushort)1, (ushort)1, 20u, 20u, 0u, 20u, 22u, 23u },
                    { (ushort)1, (ushort)2, 21u, 40u, 0u, 20u, 24u, 25u },
                    { (ushort)1, (ushort)3, 23u, 60u, 0u, 20u, 26u, 27u },
                    { (ushort)1, (ushort)4, 24u, 80u, 0u, 20u, 28u, 29u },
                    { (ushort)1, (ushort)5, 26u, 100u, 0u, 20u, 30u, 31u },
                    { (ushort)2, (ushort)1, 20u, 16u, 20u, 23u, 21u, 20u },
                    { (ushort)2, (ushort)2, 21u, 32u, 40u, 25u, 22u, 20u },
                    { (ushort)2, (ushort)3, 22u, 48u, 60u, 27u, 23u, 20u },
                    { (ushort)2, (ushort)4, 23u, 64u, 80u, 29u, 24u, 20u },
                    { (ushort)2, (ushort)5, 24u, 80u, 100u, 31u, 25u, 20u },
                    { (ushort)3, (ushort)1, 23u, 18u, 10u, 20u, 20u, 21u },
                    { (ushort)3, (ushort)2, 25u, 36u, 20u, 20u, 21u, 22u },
                    { (ushort)3, (ushort)3, 27u, 54u, 30u, 20u, 22u, 23u },
                    { (ushort)3, (ushort)4, 29u, 72u, 40u, 20u, 23u, 24u },
                    { (ushort)3, (ushort)5, 31u, 90u, 50u, 20u, 24u, 25u },
                    { (ushort)4, (ushort)1, 21u, 18u, 20u, 23u, 20u, 20u },
                    { (ushort)4, (ushort)2, 22u, 36u, 40u, 25u, 21u, 20u },
                    { (ushort)4, (ushort)3, 23u, 54u, 60u, 27u, 22u, 20u },
                    { (ushort)4, (ushort)4, 24u, 72u, 80u, 29u, 23u, 20u },
                    { (ushort)4, (ushort)5, 25u, 90u, 100u, 31u, 24u, 20u }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterCreateInfos");

            migrationBuilder.DropTable(
                name: "CharacterLevelExperiences");

            migrationBuilder.DropTable(
                name: "ClassLevelStats");
        }
    }
}
