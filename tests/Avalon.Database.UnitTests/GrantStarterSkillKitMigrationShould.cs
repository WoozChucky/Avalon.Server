using Avalon.Database.Character.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// The hand-written SQL of GrantStarterSkillKit. SQLite builds the model with EnsureCreated and never
/// runs it, so its operations are read directly.
/// </summary>
public class GrantStarterSkillKitMigrationShould
{
    [Fact]
    public void Replace_every_characters_abilities_with_its_classes_kit()
    {
        List<string> sql = new GrantStarterSkillKit().UpOperations.OfType<SqlOperation>().Select(o => o.Sql).ToList();

        int delete = sql.FindIndex(s => s.Contains("DELETE FROM \"CharacterAbilities\"", StringComparison.Ordinal));
        int insert = sql.FindIndex(s => s.Contains("INSERT INTO \"CharacterAbilities\"", StringComparison.Ordinal));

        Assert.True(delete >= 0, "no unconditional delete of the old abilities");
        Assert.True(insert > delete, "the kit is granted after the old abilities are gone");
        foreach (string pair in new[] { "(1, 200)", "(1, 201)", "(1, 202)", "(2, 210)", "(2, 211)", "(2, 212)",
                                        "(3, 220)", "(3, 221)", "(3, 222)", "(4, 230)", "(4, 231)", "(4, 232)" })
        {
            Assert.Contains(pair, sql[insert], StringComparison.Ordinal);
        }
    }
}
