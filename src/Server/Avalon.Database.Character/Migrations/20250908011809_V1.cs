using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class V1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Class = table.Column<int>(type: "integer", nullable: false),
                    Gender = table.Column<byte>(type: "smallint", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Experience = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    X = table.Column<float>(type: "real", nullable: false),
                    Y = table.Column<float>(type: "real", nullable: false),
                    Z = table.Column<float>(type: "real", nullable: false),
                    Running = table.Column<bool>(type: "boolean", nullable: false),
                    Rotation = table.Column<float>(type: "real", nullable: false),
                    Map = table.Column<int>(type: "integer", nullable: false),
                    InstanceId = table.Column<string>(type: "text", nullable: true),
                    Online = table.Column<bool>(type: "boolean", nullable: false),
                    TotalTime = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    LevelTime = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    LogoutTime = table.Column<int>(type: "integer", nullable: false),
                    IsLogoutResting = table.Column<bool>(type: "boolean", nullable: false),
                    RestBonus = table.Column<float>(type: "real", nullable: false),
                    TotalKills = table.Column<int>(type: "integer", nullable: false),
                    TodayKills = table.Column<int>(type: "integer", nullable: false),
                    YesterdayKills = table.Column<int>(type: "integer", nullable: false),
                    ChosenTitle = table.Column<int>(type: "integer", nullable: false),
                    Health = table.Column<int>(type: "integer", nullable: false),
                    Power1 = table.Column<int>(type: "integer", nullable: false),
                    Power2 = table.Column<int>(type: "integer", nullable: false),
                    Latency = table.Column<int>(type: "integer", nullable: false),
                    ActionBars = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeleteDate = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CharacterInventory",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    Container = table.Column<int>(type: "integer", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "CharacterSpells",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    SpellId = table.Column<long>(type: "bigint", nullable: false),
                    Cooldown = table.Column<long>(type: "bigint", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "CharacterStats",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    MaxHealth = table.Column<long>(type: "bigint", nullable: false),
                    MaxPower1 = table.Column<long>(type: "bigint", nullable: false),
                    MaxPower2 = table.Column<long>(type: "bigint", nullable: false),
                    Stamina = table.Column<long>(type: "bigint", nullable: false),
                    Strength = table.Column<long>(type: "bigint", nullable: false),
                    Agility = table.Column<long>(type: "bigint", nullable: false),
                    Intellect = table.Column<long>(type: "bigint", nullable: false),
                    Armor = table.Column<long>(type: "bigint", nullable: false),
                    BlockPct = table.Column<float>(type: "real", nullable: false),
                    DodgePct = table.Column<float>(type: "real", nullable: false),
                    CritPct = table.Column<float>(type: "real", nullable: false),
                    AttackDamage = table.Column<long>(type: "bigint", nullable: false),
                    AbilityDamage = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterStats", x => x.CharacterId);
                    table.ForeignKey(
                        name: "FK_CharacterStats_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
