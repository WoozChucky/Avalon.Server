using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
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
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<uint>(type: "int unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AccountId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Class = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Gender = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Level = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Experience = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    X = table.Column<float>(type: "float", nullable: false),
                    Y = table.Column<float>(type: "float", nullable: false),
                    Z = table.Column<float>(type: "float", nullable: false),
                    Running = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Rotation = table.Column<float>(type: "float", nullable: false),
                    Map = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    InstanceId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Online = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TotalTime = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    LevelTime = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    LogoutTime = table.Column<int>(type: "int", nullable: false),
                    IsLogoutResting = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RestBonus = table.Column<float>(type: "float", nullable: false),
                    TotalKills = table.Column<int>(type: "int", nullable: false),
                    TodayKills = table.Column<int>(type: "int", nullable: false),
                    YesterdayKills = table.Column<int>(type: "int", nullable: false),
                    ChosenTitle = table.Column<int>(type: "int", nullable: false),
                    Health = table.Column<int>(type: "int", nullable: false),
                    Power1 = table.Column<int>(type: "int", nullable: false),
                    Power2 = table.Column<int>(type: "int", nullable: false),
                    Latency = table.Column<int>(type: "int", nullable: false),
                    ActionBars = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeleteDate = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CharacterInventory",
                columns: table => new
                {
                    CharacterId = table.Column<uint>(type: "int unsigned", nullable: false),
                    Container = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    Slot = table.Column<ushort>(type: "smallint unsigned", nullable: false),
                    ItemId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterInventory", x => new { x.CharacterId, x.Container, x.Slot });
                    table.ForeignKey(
                        name: "FK_CharacterInventory_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CharacterSpells",
                columns: table => new
                {
                    CharacterId = table.Column<uint>(type: "int unsigned", nullable: false),
                    SpellId = table.Column<uint>(type: "int unsigned", nullable: false),
                    Cooldown = table.Column<uint>(type: "int unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterSpells", x => new { x.CharacterId, x.SpellId });
                    table.ForeignKey(
                        name: "FK_CharacterSpells_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CharacterStats",
                columns: table => new
                {
                    CharacterId = table.Column<uint>(type: "int unsigned", nullable: false),
                    MaxHealth = table.Column<uint>(type: "int unsigned", nullable: false),
                    MaxPower1 = table.Column<uint>(type: "int unsigned", nullable: false),
                    MaxPower2 = table.Column<uint>(type: "int unsigned", nullable: false),
                    Stamina = table.Column<uint>(type: "int unsigned", nullable: false),
                    Strength = table.Column<uint>(type: "int unsigned", nullable: false),
                    Agility = table.Column<uint>(type: "int unsigned", nullable: false),
                    Intellect = table.Column<uint>(type: "int unsigned", nullable: false),
                    Armor = table.Column<uint>(type: "int unsigned", nullable: false),
                    BlockPct = table.Column<float>(type: "float", nullable: false),
                    DodgePct = table.Column<float>(type: "float", nullable: false),
                    CritPct = table.Column<float>(type: "float", nullable: false),
                    AttackDamage = table.Column<uint>(type: "int unsigned", nullable: false),
                    AbilityDamage = table.Column<uint>(type: "int unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterStats", x => x.CharacterId);
                    table.CheckConstraint("CK_CharacterStats_MinValues", "MaxHealth >= 0 AND Stamina >= 0 AND Strength >= 0 AND Agility >= 0 AND Intellect >= 0");
                    table.ForeignKey(
                        name: "FK_CharacterStats_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterInventory_CharacterId",
                table: "CharacterInventory",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterSpells_CharacterId",
                table: "CharacterSpells",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterStats_CharacterId",
                table: "CharacterStats",
                column: "CharacterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterInventory");

            migrationBuilder.DropTable(
                name: "CharacterSpells");

            migrationBuilder.DropTable(
                name: "CharacterStats");

            migrationBuilder.DropTable(
                name: "Characters");
        }
    }
}
