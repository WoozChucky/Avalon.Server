using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.World.Database.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CreatureTemplates",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IconName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinLevel = table.Column<short>(type: "smallint", nullable: false),
                    MaxLevel = table.Column<short>(type: "smallint", nullable: false),
                    SpeedWalk = table.Column<float>(type: "float", nullable: false),
                    SpeedRun = table.Column<float>(type: "float", nullable: false),
                    SpeedSwim = table.Column<float>(type: "float", nullable: false),
                    Rank = table.Column<short>(type: "smallint", nullable: false),
                    Family = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Type = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Exp = table.Column<short>(type: "smallint", nullable: false),
                    LootId = table.Column<int>(type: "int", nullable: false),
                    MinGold = table.Column<int>(type: "int", nullable: false),
                    MaxGold = table.Column<int>(type: "int", nullable: false),
                    AIName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MovementType = table.Column<short>(type: "smallint", nullable: false),
                    DetectionRange = table.Column<float>(type: "float", nullable: false),
                    MovementId = table.Column<int>(type: "int", nullable: false),
                    ScriptName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HealthModifier = table.Column<float>(type: "float", nullable: false),
                    ManaModifier = table.Column<float>(type: "float", nullable: false),
                    ArmorModifier = table.Column<float>(type: "float", nullable: false),
                    ExperienceModifier = table.Column<float>(type: "float", nullable: false),
                    RegenHealth = table.Column<short>(type: "smallint", nullable: false),
                    DmgSchool = table.Column<short>(type: "smallint", nullable: false),
                    DamageModifier = table.Column<float>(type: "float", nullable: false),
                    BaseAttackTime = table.Column<int>(type: "int", nullable: false),
                    RangeAttackTime = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatureTemplates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ItemTemplates",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTemplates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MapTemplates",
                columns: table => new
                {
                    Id = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Atlas = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Directory = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InstanceType = table.Column<int>(type: "int", nullable: false),
                    PvP = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MinLevel = table.Column<int>(type: "int", nullable: false),
                    MaxLevel = table.Column<int>(type: "int", nullable: false),
                    AreaTableId = table.Column<int>(type: "int", nullable: false),
                    LoadingScreenId = table.Column<int>(type: "int", nullable: false),
                    CorpseX = table.Column<float>(type: "float", nullable: false),
                    CorpseY = table.Column<float>(type: "float", nullable: false),
                    MaxPlayers = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapTemplates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QuestRewardTemplates",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Value = table.Column<uint>(type: "int unsigned", nullable: false),
                    Count = table.Column<uint>(type: "int unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestRewardTemplates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QuestTemplates",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Environment = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Type = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Rarity = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Title = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GiverCreatureId = table.Column<int>(type: "int", nullable: false),
                    EnderCreatureId = table.Column<int>(type: "int", nullable: false),
                    CompletionCriteriaId = table.Column<int>(type: "int", nullable: false),
                    IsRepeatable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RepeatFrequency = table.Column<ushort>(type: "smallint unsigned", nullable: true),
                    LevelRequirement = table.Column<int>(type: "int", nullable: false),
                    RequiredQuestId = table.Column<int>(type: "int", nullable: false),
                    ClassRequirement = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestTemplates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QuestRewards",
                columns: table => new
                {
                    QuestId = table.Column<uint>(type: "int unsigned", nullable: false),
                    RewardId = table.Column<uint>(type: "int unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestRewards", x => new { x.QuestId, x.RewardId });
                    table.ForeignKey(
                        name: "FK_QuestRewards_QuestRewardTemplates_RewardId",
                        column: x => x.RewardId,
                        principalTable: "QuestRewardTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestRewards_QuestTemplates_QuestId",
                        column: x => x.QuestId,
                        principalTable: "QuestTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "CreatureTemplates",
                columns: new[] { "Id", "AIName", "ArmorModifier", "BaseAttackTime", "DamageModifier", "DetectionRange", "DmgSchool", "Exp", "ExperienceModifier", "Family", "HealthModifier", "IconName", "LootId", "ManaModifier", "MaxGold", "MaxLevel", "MinGold", "MinLevel", "MovementId", "MovementType", "Name", "RangeAttackTime", "Rank", "RegenHealth", "ScriptName", "SpeedRun", "SpeedSwim", "SpeedWalk", "SubName", "Type" },
                values: new object[,]
                {
                    { 1ul, "", 1f, 1, 1f, 20f, (short)0, (short)0, 1f, (ushort)0, 1f, "", 0, 1f, 0, (short)1, 0, (short)1, 0, (short)0, "Uriel", 0, (short)0, (short)1, "UrielTownPatrolScript", 35f, 18f, 30f, "", (ushort)7 },
                    { 2ul, "", 1f, 1, 1f, 20f, (short)0, (short)0, 1f, (ushort)0, 1f, "", 0, 1f, 0, (short)1, 0, (short)1, 0, (short)0, "Borin Stoutbeard", 0, (short)0, (short)1, "", 35f, 18f, 30f, "", (ushort)7 },
                    { 3ul, "", 1f, 1, 1f, 20f, (short)0, (short)0, 1f, (ushort)0, 1f, "", 0, 1f, 0, (short)1, 0, (short)1, 0, (short)0, "Innkeeper", 0, (short)0, (short)1, "", 35f, 18f, 30f, "", (ushort)7 }
                });

            migrationBuilder.InsertData(
                table: "MapTemplates",
                columns: new[] { "Id", "AreaTableId", "Atlas", "CorpseX", "CorpseY", "Description", "Directory", "InstanceType", "LoadingScreenId", "MaxLevel", "MaxPlayers", "MinLevel", "Name", "PvP" },
                values: new object[,]
                {
                    { (ushort)1, 0, "Serene_Village_32x32", 0f, 0f, "Glimmerdell", "Maps/", 0, 0, 60, 32, 1, "Tutorial.tmx", false },
                    { (ushort)2, 0, "Serene_Village_32x32", 0f, 0f, "Ebonheart Woods", "Maps/", 2, 0, 20, 5, 1, "Village.tmx", false }
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestRewards_RewardId",
                table: "QuestRewards",
                column: "RewardId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreatureTemplates");

            migrationBuilder.DropTable(
                name: "ItemTemplates");

            migrationBuilder.DropTable(
                name: "MapTemplates");

            migrationBuilder.DropTable(
                name: "QuestRewards");

            migrationBuilder.DropTable(
                name: "QuestRewardTemplates");

            migrationBuilder.DropTable(
                name: "QuestTemplates");
        }
    }
}
