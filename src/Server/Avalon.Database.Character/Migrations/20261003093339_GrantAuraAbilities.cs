using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class GrantAuraAbilities : Migration
    {
        /// <summary>
        /// Auras: each existing character gets its class's new abilities (Warrior 203, Wizard 213, Hunter 223, Healer 233
        /// and 234), cooldown 0, unless it already holds one; nothing else is touched. Class values: 1 Warrior, 2 Wizard,
        /// 3 Hunter, 4 Healer.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "INSERT INTO \"CharacterAbilities\" (\"CharacterId\", \"AbilityId\", \"Cooldown\") " +
                "SELECT c.\"Id\", k.ability, 0 FROM \"Characters\" c JOIN (VALUES " +
                "(1, 203), (2, 213), (3, 223), (4, 233), (4, 234)" +
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
                "(1, 203), (2, 213), (3, 223), (4, 233), (4, 234));");
        }
    }
}
