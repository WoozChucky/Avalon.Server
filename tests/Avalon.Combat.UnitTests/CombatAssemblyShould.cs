using System.Reflection;

namespace Avalon.Combat.UnitTests;

/// <summary>
/// Avalon.Combat is the pure combat maths the world server, the API and the balance simulator share
/// (balance workbench, sub-project 1). It must never grow a dependency on the world server itself.
/// </summary>
public class CombatAssemblyShould
{
    private static readonly Assembly Combat = typeof(HitResolver).Assembly;

    [Fact]
    public void Not_reference_the_world_server_or_a_database()
    {
        string[] referenced = Combat.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(referenced, n => n == "Avalon.World");
        Assert.DoesNotContain(referenced, n => n.StartsWith("Avalon.Database", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(typeof(HitResolver))]
    [InlineData(typeof(AttackerCombat))]
    [InlineData(typeof(CombatRandom))]
    [InlineData(typeof(Haste))]
    [InlineData(typeof(HealRules))]
    [InlineData(typeof(PowerPool))]
    [InlineData(typeof(Fury))]
    [InlineData(typeof(DerivedCharacterStats))]
    [InlineData(typeof(CharacterStatsCalculator))]
    [InlineData(typeof(CreatureStatDeriver))]
    [InlineData(typeof(AbilityAmounts))]
    [InlineData(typeof(AbilityCost))]
    [InlineData(typeof(AbilityMetadataMapper))]
    [InlineData(typeof(AbilityAmountMath))]
    [InlineData(typeof(PowerRegen))]
    [InlineData(typeof(RegenConfiguration))]
    public void Hold_every_moved_rule(Type type)
    {
        Assert.Same(Combat, type.Assembly);
        Assert.Equal("Avalon.Combat", type.Namespace);
    }
}
