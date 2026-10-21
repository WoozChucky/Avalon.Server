using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class GrantCrowdControlAbilities : Migration
    {
        /// <summary>
        /// Crowd control (#770): each existing character gets its class's new ability (Warrior 204 Shield Bash, Wizard
        /// 214 Frost Bind, Hunter 224 Binding Shot, Healer 235 Hush), cooldown 0, unless it already holds it; nothing
        /// else is touched. Class values: 1 Warrior, 2 Wizard, 3 Hunter, 4 Healer.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "INSERT INTO \"CharacterAbilities\" (\"CharacterId\", \"AbilityId\", \"Cooldown\") " +
                "SELECT c.\"Id\", k.ability, 0 FROM \"Characters\" c JOIN (VALUES " +
                "(1, 204), (2, 214), (3, 224), (4, 235)" +
                ") AS k(class, ability) ON c.\"Class\" = k.class " +
                "WHERE NOT EXISTS (SELECT 1 FROM \"CharacterAbilities\" a " +
                "WHERE a.\"CharacterId\" = c.\"Id\" AND a.\"AbilityId\" = k.ability);");
        }

        /// <summary>
        /// Takes back exactly those abilities from characters of those classes, a matching pair held before Up included:
        /// Up records nothing about which rows it inserted.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM \"CharacterAbilities\" a USING \"Characters\" c " +
                "WHERE a.\"CharacterId\" = c.\"Id\" AND (c.\"Class\", a.\"AbilityId\") IN (" +
                "(1, 204), (2, 214), (3, 224), (4, 235));");
        }
    }
}
