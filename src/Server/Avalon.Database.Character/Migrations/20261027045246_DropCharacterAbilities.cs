using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class DropCharacterAbilities : Migration
    {
        /// <summary>
        /// The talent tree (#1181): a character's abilities are its class start's starter and the ability nodes it holds,
        /// so the per-character ability rows go. With the empty CharacterTalents table AddCharacterTalents made, this is
        /// the release reset: every character starts the tree with its starter alone and every point to spend.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterAbilities");
        }

        /// <summary>
        /// Back to the per-character ability rows, every character granted its class's whole pre-tree kit, cooldown 0, so a
        /// rollback leaves nobody without abilities: Warrior 200-204, Wizard 210-214, Hunter 220-224, Cleric 230-235.
        /// Class values: 1 Warrior, 2 Wizard, 3 Hunter, 4 Cleric.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterAbilities",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    AbilityId = table.Column<long>(type: "bigint", nullable: false),
                    Cooldown = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterAbilities", x => new { x.CharacterId, x.AbilityId });
                    table.ForeignKey(
                        name: "FK_CharacterAbilities_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterAbilities_CharacterId",
                table: "CharacterAbilities",
                column: "CharacterId");

            migrationBuilder.Sql(
                "INSERT INTO \"CharacterAbilities\" (\"CharacterId\", \"AbilityId\", \"Cooldown\") " +
                "SELECT c.\"Id\", k.ability, 0 FROM \"Characters\" c JOIN (VALUES " +
                "(1, 200), (1, 201), (1, 202), (1, 203), (1, 204), " +
                "(2, 210), (2, 211), (2, 212), (2, 213), (2, 214), " +
                "(3, 220), (3, 221), (3, 222), (3, 223), (3, 224), " +
                "(4, 230), (4, 231), (4, 232), (4, 233), (4, 234), (4, 235)" +
                ") AS k(class, ability) ON c.\"Class\" = k.class;");
        }
    }
}
