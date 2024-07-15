using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.World.Database.Migrations
{
    /// <inheritdoc />
    public partial class V1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CharacterCreateInfos",
                columns: table => new
                {
                    Class = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Map = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    X = table.Column<float>(type: "float", nullable: false),
                    Y = table.Column<float>(type: "float", nullable: false),
                    Z = table.Column<float>(type: "float", nullable: false),
                    Rotation = table.Column<float>(type: "float", nullable: false),
                    StartingItems = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartingSpells = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Class = table.Column<int>(type: "int", nullable: false),
                    SubClass = table.Column<int>(type: "int", nullable: false),
                    Flags = table.Column<int>(type: "int", nullable: false),
                    MaxStackSize = table.Column<uint>(type: "int unsigned", nullable: false),
                    DisplayId = table.Column<uint>(type: "int unsigned", nullable: false),
                    Rarity = table.Column<int>(type: "int", nullable: false),
                    BuyPrice = table.Column<uint>(type: "int unsigned", nullable: false),
                    SellPrice = table.Column<uint>(type: "int unsigned", nullable: false),
                    Slot = table.Column<ushort>(type: "smallint unsigned", nullable: true),
                    AllowedClasses = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ItemPower = table.Column<ushort>(type: "smallint unsigned", nullable: true),
                    RequiredLevel = table.Column<ushort>(type: "smallint unsigned", nullable: true),
                    DamageMin1 = table.Column<uint>(type: "int unsigned", nullable: true),
                    DamageMax1 = table.Column<uint>(type: "int unsigned", nullable: true),
                    DamageType1 = table.Column<int>(type: "int", nullable: true),
                    DamageMin2 = table.Column<uint>(type: "int unsigned", nullable: true),
                    DamageMax2 = table.Column<uint>(type: "int unsigned", nullable: true),
                    DamageType2 = table.Column<int>(type: "int", nullable: true),
                    StatType1 = table.Column<int>(type: "int", nullable: true),
                    StatValue1 = table.Column<uint>(type: "int unsigned", nullable: true),
                    StatType2 = table.Column<int>(type: "int", nullable: true),
                    StatValue2 = table.Column<uint>(type: "int unsigned", nullable: true),
                    StatType3 = table.Column<int>(type: "int", nullable: true),
                    StatValue3 = table.Column<uint>(type: "int unsigned", nullable: true),
                    StatType4 = table.Column<int>(type: "int", nullable: true),
                    StatValue4 = table.Column<uint>(type: "int unsigned", nullable: true),
                    StatType5 = table.Column<int>(type: "int", nullable: true),
                    StatValue5 = table.Column<uint>(type: "int unsigned", nullable: true),
                    StatType6 = table.Column<int>(type: "int", nullable: true),
                    StatValue6 = table.Column<uint>(type: "int unsigned", nullable: true),
                    StatType7 = table.Column<int>(type: "int", nullable: true),
                    StatValue7 = table.Column<uint>(type: "int unsigned", nullable: true),
                    StatType8 = table.Column<int>(type: "int", nullable: true),
                    StatValue8 = table.Column<uint>(type: "int unsigned", nullable: true),
                    StatType9 = table.Column<int>(type: "int", nullable: true),
                    StatValue9 = table.Column<uint>(type: "int unsigned", nullable: true),
                    StatType10 = table.Column<int>(type: "int", nullable: true),
                    StatValue10 = table.Column<uint>(type: "int unsigned", nullable: true)
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
                    Directory = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InstanceType = table.Column<int>(type: "int", nullable: false),
                    PvP = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MinLevel = table.Column<ushort>(type: "smallint unsigned", nullable: true),
                    MaxLevel = table.Column<ushort>(type: "smallint unsigned", nullable: true),
                    AreaTableId = table.Column<int>(type: "int", nullable: false),
                    LoadingScreenId = table.Column<int>(type: "int", nullable: false),
                    CorpseX = table.Column<float>(type: "float", nullable: true),
                    CorpseY = table.Column<float>(type: "float", nullable: true),
                    CorpseZ = table.Column<float>(type: "float", nullable: true),
                    MaxPlayers = table.Column<ushort>(type: "smallint unsigned", nullable: true)
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
                name: "SpellTemplates",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "int unsigned", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CastTime = table.Column<uint>(type: "int unsigned", nullable: false),
                    Cooldown = table.Column<uint>(type: "int unsigned", nullable: false),
                    Cost = table.Column<uint>(type: "int unsigned", nullable: false),
                    Range = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Effects = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    EffectValue = table.Column<uint>(type: "int unsigned", nullable: false),
                    AllowedClasses = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpellTemplates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ItemInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TemplateId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    CharacterId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Count = table.Column<uint>(type: "int unsigned", nullable: false),
                    Durability = table.Column<uint>(type: "int unsigned", nullable: false),
                    Charges = table.Column<uint>(type: "int unsigned", nullable: false),
                    Flags = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemInstances_ItemTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                table: "CharacterCreateInfos",
                columns: new[] { "Class", "Map", "Rotation", "StartingItems", "StartingSpells", "X", "Y", "Z" },
                values: new object[,]
                {
                    { (ushort)1, (ushort)1, 0f, "1,2,3", "1", 25f, 51f, 25f },
                    { (ushort)2, (ushort)1, 0f, "1,2", "2", 25f, 51f, 25f },
                    { (ushort)3, (ushort)1, 0f, "1,2", "", 25f, 51f, 25f },
                    { (ushort)4, (ushort)1, 0f, "1,2", "", 25f, 51f, 25f }
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
                table: "ItemTemplates",
                columns: new[] { "Id", "AllowedClasses", "BuyPrice", "Class", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2", "DisplayId", "Flags", "ItemPower", "MaxStackSize", "Name", "Rarity", "RequiredLevel", "SellPrice", "Slot", "StatType1", "StatType10", "StatType2", "StatType3", "StatType4", "StatType5", "StatType6", "StatType7", "StatType8", "StatType9", "StatValue1", "StatValue10", "StatValue2", "StatValue3", "StatValue4", "StatValue5", "StatValue6", "StatValue7", "StatValue8", "StatValue9", "SubClass" },
                values: new object[,]
                {
                    { 1ul, "Warrior,Wizard,Hunter,Healer", 10u, 0, null, null, null, null, null, null, 1u, 256, null, 40u, "Health Potion", 1, null, 5u, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 0 },
                    { 2ul, "Warrior,Wizard,Hunter,Healer", 13u, 0, null, null, null, null, null, null, 2u, 256, null, 40u, "Mana Potion", 1, null, 6u, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 0 },
                    { 3ul, "Warrior,Wizard,Hunter,Healer", 100u, 0, null, null, null, null, null, null, 3u, 256, null, 40u, "Town Portal Scroll", 1, null, 50u, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 2 },
                    { 4ul, "Warrior", 100u, 1, 3u, null, 1u, null, 0, null, 4u, 256, (ushort)2, 1u, "Rusted Sword", 1, (ushort)1, 50u, (ushort)9, 12, null, null, null, null, null, null, null, null, null, 13u, null, null, null, null, null, null, null, null, null, 100 }
                });

            migrationBuilder.InsertData(
                table: "MapTemplates",
                columns: new[] { "Id", "AreaTableId", "CorpseX", "CorpseY", "CorpseZ", "Description", "Directory", "InstanceType", "LoadingScreenId", "MaxLevel", "MaxPlayers", "MinLevel", "Name", "PvP" },
                values: new object[] { (ushort)1, 0, 0f, 0f, null, "Glimmerdell", "Maps/", 0, 0, (ushort)60, (ushort)32, (ushort)1, "world.bin", false });

            migrationBuilder.InsertData(
                table: "SpellTemplates",
                columns: new[] { "Id", "AllowedClasses", "CastTime", "Cooldown", "Cost", "EffectValue", "Effects", "Name", "Range" },
                values: new object[,]
                {
                    { 1u, "[1]", 0u, 2500u, 25u, 10u, (ushort)1, "Strike", (ushort)1 },
                    { 2u, "[2]", 1000u, 1000u, 10u, 10u, (ushort)1, "Lightning Bolt", (ushort)10 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstances_TemplateId",
                table: "ItemInstances",
                column: "TemplateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuestRewards_RewardId",
                table: "QuestRewards",
                column: "RewardId");
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

            migrationBuilder.DropTable(
                name: "CreatureTemplates");

            migrationBuilder.DropTable(
                name: "ItemInstances");

            migrationBuilder.DropTable(
                name: "MapTemplates");

            migrationBuilder.DropTable(
                name: "QuestRewards");

            migrationBuilder.DropTable(
                name: "SpellTemplates");

            migrationBuilder.DropTable(
                name: "ItemTemplates");

            migrationBuilder.DropTable(
                name: "QuestRewardTemplates");

            migrationBuilder.DropTable(
                name: "QuestTemplates");
        }
    }
}
