using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Avalon.Database.World.Migrations
{
    /// <inheritdoc />
    public partial class V1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterCreateInfos",
                columns: table => new
                {
                    Class = table.Column<int>(type: "integer", nullable: false),
                    Map = table.Column<int>(type: "integer", nullable: false),
                    X = table.Column<float>(type: "real", nullable: false),
                    Y = table.Column<float>(type: "real", nullable: false),
                    Z = table.Column<float>(type: "real", nullable: false),
                    Rotation = table.Column<float>(type: "real", nullable: false),
                    StartingItems = table.Column<string>(type: "text", nullable: false),
                    StartingSpells = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterCreateInfos", x => x.Class);
                });

            migrationBuilder.CreateTable(
                name: "CharacterLevelExperiences",
                columns: table => new
                {
                    Level = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Experience = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterLevelExperiences", x => x.Level);
                });

            migrationBuilder.CreateTable(
                name: "ClassLevelStats",
                columns: table => new
                {
                    Class = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    BaseHp = table.Column<long>(type: "bigint", nullable: false),
                    BaseMana = table.Column<long>(type: "bigint", nullable: false),
                    Stamina = table.Column<long>(type: "bigint", nullable: false),
                    Strength = table.Column<long>(type: "bigint", nullable: false),
                    Agility = table.Column<long>(type: "bigint", nullable: false),
                    Intellect = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassLevelStats", x => new { x.Class, x.Level });
                });

            migrationBuilder.CreateTable(
                name: "CreatureTemplates",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SubName = table.Column<string>(type: "text", nullable: false),
                    IconName = table.Column<string>(type: "text", nullable: false),
                    MinLevel = table.Column<short>(type: "smallint", nullable: false),
                    MaxLevel = table.Column<short>(type: "smallint", nullable: false),
                    SpeedWalk = table.Column<float>(type: "real", nullable: false),
                    SpeedRun = table.Column<float>(type: "real", nullable: false),
                    SpeedSwim = table.Column<float>(type: "real", nullable: false),
                    Rank = table.Column<short>(type: "smallint", nullable: false),
                    Family = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Exp = table.Column<short>(type: "smallint", nullable: false),
                    LootId = table.Column<int>(type: "integer", nullable: false),
                    MinGold = table.Column<int>(type: "integer", nullable: false),
                    MaxGold = table.Column<int>(type: "integer", nullable: false),
                    AIName = table.Column<string>(type: "text", nullable: false),
                    MovementType = table.Column<short>(type: "smallint", nullable: false),
                    DetectionRange = table.Column<float>(type: "real", nullable: false),
                    MovementId = table.Column<int>(type: "integer", nullable: false),
                    ScriptName = table.Column<string>(type: "text", nullable: false),
                    HealthModifier = table.Column<float>(type: "real", nullable: false),
                    ManaModifier = table.Column<float>(type: "real", nullable: false),
                    ArmorModifier = table.Column<float>(type: "real", nullable: false),
                    ExperienceModifier = table.Column<float>(type: "real", nullable: false),
                    RegenHealth = table.Column<short>(type: "smallint", nullable: false),
                    DmgSchool = table.Column<short>(type: "smallint", nullable: false),
                    DamageModifier = table.Column<float>(type: "real", nullable: false),
                    BaseAttackTime = table.Column<int>(type: "integer", nullable: false),
                    RangeAttackTime = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatureTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemTemplates",
                columns: table => new
                {
                    Id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Class = table.Column<int>(type: "integer", nullable: false),
                    SubClass = table.Column<int>(type: "integer", nullable: false),
                    Flags = table.Column<int>(type: "integer", nullable: false),
                    MaxStackSize = table.Column<long>(type: "bigint", nullable: false),
                    DisplayId = table.Column<long>(type: "bigint", nullable: false),
                    Rarity = table.Column<int>(type: "integer", nullable: false),
                    BuyPrice = table.Column<long>(type: "bigint", nullable: false),
                    SellPrice = table.Column<long>(type: "bigint", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: true),
                    AllowedClasses = table.Column<string>(type: "text", nullable: false),
                    ItemPower = table.Column<int>(type: "integer", nullable: true),
                    RequiredLevel = table.Column<int>(type: "integer", nullable: true),
                    DamageMin1 = table.Column<long>(type: "bigint", nullable: true),
                    DamageMax1 = table.Column<long>(type: "bigint", nullable: true),
                    DamageType1 = table.Column<int>(type: "integer", nullable: true),
                    DamageMin2 = table.Column<long>(type: "bigint", nullable: true),
                    DamageMax2 = table.Column<long>(type: "bigint", nullable: true),
                    DamageType2 = table.Column<int>(type: "integer", nullable: true),
                    StatType1 = table.Column<int>(type: "integer", nullable: true),
                    StatValue1 = table.Column<long>(type: "bigint", nullable: true),
                    StatType2 = table.Column<int>(type: "integer", nullable: true),
                    StatValue2 = table.Column<long>(type: "bigint", nullable: true),
                    StatType3 = table.Column<int>(type: "integer", nullable: true),
                    StatValue3 = table.Column<long>(type: "bigint", nullable: true),
                    StatType4 = table.Column<int>(type: "integer", nullable: true),
                    StatValue4 = table.Column<long>(type: "bigint", nullable: true),
                    StatType5 = table.Column<int>(type: "integer", nullable: true),
                    StatValue5 = table.Column<long>(type: "bigint", nullable: true),
                    StatType6 = table.Column<int>(type: "integer", nullable: true),
                    StatValue6 = table.Column<long>(type: "bigint", nullable: true),
                    StatType7 = table.Column<int>(type: "integer", nullable: true),
                    StatValue7 = table.Column<long>(type: "bigint", nullable: true),
                    StatType8 = table.Column<int>(type: "integer", nullable: true),
                    StatValue8 = table.Column<long>(type: "bigint", nullable: true),
                    StatType9 = table.Column<int>(type: "integer", nullable: true),
                    StatValue9 = table.Column<long>(type: "bigint", nullable: true),
                    StatType10 = table.Column<int>(type: "integer", nullable: true),
                    StatValue10 = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MapTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Directory = table.Column<string>(type: "text", nullable: false),
                    InstanceType = table.Column<int>(type: "integer", nullable: false),
                    PvP = table.Column<bool>(type: "boolean", nullable: false),
                    MinLevel = table.Column<int>(type: "integer", nullable: true),
                    MaxLevel = table.Column<int>(type: "integer", nullable: true),
                    AreaTableId = table.Column<int>(type: "integer", nullable: false),
                    LoadingScreenId = table.Column<int>(type: "integer", nullable: false),
                    CorpseX = table.Column<float>(type: "real", nullable: true),
                    CorpseY = table.Column<float>(type: "real", nullable: true),
                    CorpseZ = table.Column<float>(type: "real", nullable: true),
                    MaxPlayers = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuestRewardTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestRewardTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuestTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Rarity = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    GiverCreatureId = table.Column<int>(type: "integer", nullable: false),
                    EnderCreatureId = table.Column<int>(type: "integer", nullable: false),
                    CompletionCriteriaId = table.Column<int>(type: "integer", nullable: false),
                    IsRepeatable = table.Column<bool>(type: "boolean", nullable: false),
                    RepeatFrequency = table.Column<int>(type: "integer", nullable: true),
                    LevelRequirement = table.Column<int>(type: "integer", nullable: false),
                    RequiredQuestId = table.Column<int>(type: "integer", nullable: false),
                    ClassRequirement = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpellTemplates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CastTime = table.Column<long>(type: "bigint", nullable: false),
                    Cooldown = table.Column<long>(type: "bigint", nullable: false),
                    Cost = table.Column<long>(type: "bigint", nullable: false),
                    SpellScript = table.Column<string>(type: "text", nullable: false),
                    Range = table.Column<int>(type: "integer", nullable: false),
                    Effects = table.Column<int>(type: "integer", nullable: false),
                    EffectValue = table.Column<long>(type: "bigint", nullable: false),
                    AllowedClasses = table.Column<int[]>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpellTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    Durability = table.Column<long>(type: "bigint", nullable: false),
                    Charges = table.Column<long>(type: "bigint", nullable: false),
                    Flags = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "QuestRewards",
                columns: table => new
                {
                    QuestId = table.Column<long>(type: "bigint", nullable: false),
                    RewardId = table.Column<long>(type: "bigint", nullable: false)
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
                });

            migrationBuilder.InsertData(
                table: "CharacterCreateInfos",
                columns: new[] { "Class", "Map", "Rotation", "StartingItems", "StartingSpells", "X", "Y", "Z" },
                values: new object[,]
                {
                    { 1, 1, 0f, "1,2,3", "1,2", 25f, 51f, 25f },
                    { 2, 1, 0f, "1,2", "2", 25f, 51f, 25f },
                    { 3, 1, 0f, "1,2", "", 25f, 51f, 25f },
                    { 4, 1, 0f, "1,2", "", 25f, 51f, 25f }
                });

            migrationBuilder.InsertData(
                table: "CharacterLevelExperiences",
                columns: new[] { "Level", "Experience" },
                values: new object[,]
                {
                    { 1, 400m },
                    { 2, 900m },
                    { 3, 1400m },
                    { 4, 2100m },
                    { 5, 2800m },
                    { 6, 3600m },
                    { 7, 4500m },
                    { 8, 5400m },
                    { 9, 6500m },
                    { 10, 7600m },
                    { 11, 8700m },
                    { 12, 9800m },
                    { 13, 11000m },
                    { 14, 12300m },
                    { 15, 13600m }
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
                    { 2, 1, 20L, 16L, 20L, 23L, 21L, 20L },
                    { 2, 2, 21L, 32L, 40L, 25L, 22L, 20L },
                    { 2, 3, 22L, 48L, 60L, 27L, 23L, 20L },
                    { 2, 4, 23L, 64L, 80L, 29L, 24L, 20L },
                    { 2, 5, 24L, 80L, 100L, 31L, 25L, 20L },
                    { 3, 1, 23L, 18L, 10L, 20L, 20L, 21L },
                    { 3, 2, 25L, 36L, 20L, 20L, 21L, 22L },
                    { 3, 3, 27L, 54L, 30L, 20L, 22L, 23L },
                    { 3, 4, 29L, 72L, 40L, 20L, 23L, 24L },
                    { 3, 5, 31L, 90L, 50L, 20L, 24L, 25L },
                    { 4, 1, 21L, 18L, 20L, 23L, 20L, 20L },
                    { 4, 2, 22L, 36L, 40L, 25L, 21L, 20L },
                    { 4, 3, 23L, 54L, 60L, 27L, 22L, 20L },
                    { 4, 4, 24L, 72L, 80L, 29L, 23L, 20L },
                    { 4, 5, 25L, 90L, 100L, 31L, 24L, 20L }
                });

            migrationBuilder.InsertData(
                table: "CreatureTemplates",
                columns: new[] { "Id", "AIName", "ArmorModifier", "BaseAttackTime", "DamageModifier", "DetectionRange", "DmgSchool", "Exp", "ExperienceModifier", "Family", "HealthModifier", "IconName", "LootId", "ManaModifier", "MaxGold", "MaxLevel", "MinGold", "MinLevel", "MovementId", "MovementType", "Name", "RangeAttackTime", "Rank", "RegenHealth", "ScriptName", "SpeedRun", "SpeedSwim", "SpeedWalk", "SubName", "Type" },
                values: new object[,]
                {
                    { 1m, "", 1f, 1, 1f, 20f, (short)0, (short)0, 1f, 0, 1f, "", 0, 1f, 0, (short)1, 0, (short)1, 0, (short)0, "Uriel", 0, (short)0, (short)1, "UrielTownPatrolScript", 5f, 1.6f, 2f, "", 7 },
                    { 2m, "", 1f, 1, 1f, 20f, (short)0, (short)0, 1f, 0, 1f, "", 0, 1f, 0, (short)1, 0, (short)1, 0, (short)0, "Borin Stoutbeard", 0, (short)0, (short)1, "UrielPathfinderScript", 5f, 1.6f, 2f, "", 7 },
                    { 3m, "", 1f, 1, 1f, 20f, (short)0, (short)0, 1f, 0, 1f, "", 0, 1f, 0, (short)1, 0, (short)1, 0, (short)0, "Innkeeper", 0, (short)0, (short)1, "", 5f, 1.6f, 2f, "", 7 }
                });

            migrationBuilder.InsertData(
                table: "ItemTemplates",
                columns: new[] { "Id", "AllowedClasses", "BuyPrice", "Class", "DamageMax1", "DamageMax2", "DamageMin1", "DamageMin2", "DamageType1", "DamageType2", "DisplayId", "Flags", "ItemPower", "MaxStackSize", "Name", "Rarity", "RequiredLevel", "SellPrice", "Slot", "StatType1", "StatType10", "StatType2", "StatType3", "StatType4", "StatType5", "StatType6", "StatType7", "StatType8", "StatType9", "StatValue1", "StatValue10", "StatValue2", "StatValue3", "StatValue4", "StatValue5", "StatValue6", "StatValue7", "StatValue8", "StatValue9", "SubClass" },
                values: new object[,]
                {
                    { 1m, "Warrior,Wizard,Hunter,Healer", 10L, 0, null, null, null, null, null, null, 1L, 256, null, 40L, "Health Potion", 1, null, 5L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 0 },
                    { 2m, "Warrior,Wizard,Hunter,Healer", 13L, 0, null, null, null, null, null, null, 2L, 256, null, 40L, "Mana Potion", 1, null, 6L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 0 },
                    { 3m, "Warrior,Wizard,Hunter,Healer", 100L, 0, null, null, null, null, null, null, 3L, 256, null, 40L, "Town Portal Scroll", 1, null, 50L, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 2 },
                    { 4m, "Warrior", 100L, 1, 3L, null, 1L, null, 0, null, 4L, 256, 2, 1L, "Rusted Sword", 1, 1, 50L, 9, 12, null, null, null, null, null, null, null, null, null, 13L, null, null, null, null, null, null, null, null, null, 100 }
                });

            migrationBuilder.InsertData(
                table: "MapTemplates",
                columns: new[] { "Id", "AreaTableId", "CorpseX", "CorpseY", "CorpseZ", "Description", "Directory", "InstanceType", "LoadingScreenId", "MaxLevel", "MaxPlayers", "MinLevel", "Name", "PvP" },
                values: new object[] { 1, 0, 0f, 0f, null, "Glimmerdell", "Maps/", 0, 0, 60, 32, 1, "world.bin", false });

            migrationBuilder.InsertData(
                table: "SpellTemplates",
                columns: new[] { "Id", "AllowedClasses", "CastTime", "Cooldown", "Cost", "EffectValue", "Effects", "Name", "Range", "SpellScript" },
                values: new object[,]
                {
                    { 1L, new[] { 1 }, 0L, 2500L, 25L, 10L, 1, "Strike", 1, "StrikeSpellScript" },
                    { 2L, new[] { 1, 2 }, 2000L, 1000L, 10L, 10L, 1, "Fireball", 10, "FireballSpellScript" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstances_TemplateId",
                table: "ItemInstances",
                column: "TemplateId");

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
