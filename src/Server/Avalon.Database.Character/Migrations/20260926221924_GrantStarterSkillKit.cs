using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class GrantStarterSkillKit : Migration
    {
        /// <summary>
        /// #164: every character's abilities become its class's three kit skills. The retired ids (1, 2,
        /// 100-103) no longer exist in the World database, so a character holding them would enter the world
        /// with no skills. Class values: 1 Warrior, 2 Wizard, 3 Hunter, 4 Healer.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"CharacterAbilities\";");
            migrationBuilder.Sql(
                "INSERT INTO \"CharacterAbilities\" (\"CharacterId\", \"AbilityId\", \"Cooldown\") " +
                "SELECT c.\"Id\", k.ability, 0 FROM \"Characters\" c JOIN (VALUES " +
                "(1, 200), (1, 201), (1, 202), (2, 210), (2, 211), (2, 212), " +
                "(3, 220), (3, 221), (3, 222), (4, 230), (4, 231), (4, 232)" +
                ") AS k(class, ability) ON c.\"Class\" = k.class;");
        }

        /// <summary>Back to the retired per-class abilities the old StartingSpells granted.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"CharacterAbilities\";");
            migrationBuilder.Sql(
                "INSERT INTO \"CharacterAbilities\" (\"CharacterId\", \"AbilityId\", \"Cooldown\") " +
                "SELECT c.\"Id\", k.ability, 0 FROM \"Characters\" c JOIN (VALUES " +
                "(1, 1), (1, 2), (1, 100), (2, 2), (2, 101), (3, 102), (4, 103)" +
                ") AS k(class, ability) ON c.\"Class\" = k.class;");
        }
    }
}
