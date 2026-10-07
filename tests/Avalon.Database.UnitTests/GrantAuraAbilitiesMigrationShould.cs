using Avalon.Database.Character.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// The hand-written SQL of GrantAuraAbilities. SQLite builds the model with EnsureCreated and never runs it, so its
/// operations are read directly; running it on Postgres is checked by hand.
/// </summary>
public class GrantAuraAbilitiesMigrationShould
{
    private static readonly string[] s_pairs = ["(1, 203)", "(2, 213)", "(3, 223)", "(4, 233)", "(4, 234)"];

    [Fact]
    public void Add_each_classes_new_abilities_and_touch_no_other_row()
    {
        string up = Assert.Single(new GrantAuraAbilities().UpOperations.OfType<SqlOperation>()).Sql;

        Assert.StartsWith("INSERT INTO \"CharacterAbilities\"", up, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", up, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", up, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE", up, StringComparison.Ordinal);
        Assert.All(s_pairs, pair => Assert.Contains(pair, up, StringComparison.Ordinal));
    }

    [Fact]
    public void Take_back_only_those_abilities()
    {
        string down = Assert.Single(new GrantAuraAbilities().DownOperations.OfType<SqlOperation>()).Sql;

        Assert.StartsWith("DELETE FROM \"CharacterAbilities\"", down, StringComparison.Ordinal);
        Assert.All(s_pairs, pair => Assert.Contains(pair, down, StringComparison.Ordinal));
    }
}
