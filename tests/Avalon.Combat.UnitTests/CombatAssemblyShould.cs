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

        // Avalon.World.Public (the modding API's types) is the one world assembly Domain already brings.
        Assert.DoesNotContain(referenced, n => n.StartsWith("Avalon.World", StringComparison.Ordinal) && n != "Avalon.World.Public");
        Assert.DoesNotContain(referenced, n => n.StartsWith("Avalon.Server", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Avalon.Database", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Reference_only_the_domain_project()
    {
        string csproj = Path.Combine(RepositoryRoot(), "src", "Server", "Avalon.Combat", "Avalon.Combat.csproj");
        string[] references = System.Xml.Linq.XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(r => Path.GetFileName(r.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToArray();

        Assert.Equal(["Avalon.Domain.csproj"], references);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Avalon.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Avalon.sln not found above " + AppContext.BaseDirectory);
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
