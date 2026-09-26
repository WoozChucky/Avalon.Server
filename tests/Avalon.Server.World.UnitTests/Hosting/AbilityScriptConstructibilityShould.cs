using Avalon.Common.Mathematics;
using Avalon.Database.World;
using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Hosting;

/// <summary>
/// Every script a seeded ability names is found by ScriptManager and builds from exactly the arguments
/// InstanceAbilityCastSystem passes (ability, caster, aim, arena), through the production container (#164).
/// A constructor that wanted anything else would throw on the first cast and be logged, not caught here.
/// Seed data names scripts by string, so nothing at compile time can catch it.
/// </summary>
public class AbilityScriptConstructibilityShould
{
    private static string[] SeededScriptNames()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        return context.AbilityTemplates.AsNoTracking().Select(a => a.SpellScript).Distinct().ToList().Order().ToArray();
    }

    public static TheoryData<string> SeededScripts() => new(SeededScriptNames());

    [Theory]
    [MemberData(nameof(SeededScripts))]
    public async Task Build_every_seeded_script_from_the_cast_systems_arguments(string scriptName)
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
            builder.Services.AddWorldServices();
            using IHost host = builder.Build();

            IScriptManager scripts = host.Services.GetRequiredService<IScriptManager>();
            scripts.Load();
            Type? type = scripts.GetAbilityScript(scriptName);
            Assert.NotNull(type);

            var ability = Substitute.For<IAbility>();
            ability.Metadata.Returns(new AbilityMetadata { Name = scriptName, ScriptName = scriptName });

            // Argument-for-argument identical to InstanceAbilityCastSystem.Build.
            object built = ActivatorUtilities.CreateInstance(host.Services, type!, ability, Substitute.For<IUnit>(),
                new AbilityAim(new Vector3(0f, 0f, 1f), null), Substitute.For<IAbilityArena>());

            Assert.IsAssignableFrom<AbilityScript>(built);
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    /// <summary>The theory above passes vacuously if the seed names nothing, so pin the three shape scripts.</summary>
    [Fact]
    public void Find_the_shape_scripts_the_seed_names()
    {
        Assert.Equal(["CircleAbilityScript", "ConeAbilityScript", "ProjectileAbilityScript"], SeededScriptNames());
    }
}
