using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class V3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterStats",
                columns: table => new
                {
                    CharacterId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterStats");
        }
    }
}
