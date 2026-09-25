using System.Reflection;
using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.World;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Hosting;

/// <summary>
/// Issues #421 and #437. <c>CreaturePlacementService.AttachScript</c> builds every AI script from a
/// <c>CreatureTemplate.ScriptName</c> string with
/// <c>ActivatorUtilities.CreateInstance(sp, type, creature, instance)</c> — exactly two runtime
/// arguments, everything else from the production container. A script whose constructor needs
/// anything else throws there, the throw is swallowed into a log line, and the creature reaches the
/// world with no AI at all. Nothing at compile time can catch it, because seed data names scripts by
/// string. This walks every script the loader will resolve by name, against the real container.
/// </summary>
public class AiScriptConstructibilityShould
{
    private static string[] NameableScriptNames()
    {
        var manager = new ScriptManager(NullLoggerFactory.Instance);
        manager.Load();

        return typeof(ScriptManager).Assembly.GetTypes()
            .Where(t => typeof(AiScript).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => manager.GetAiScript(t.Name) == t)
            .Select(t => t.Name)
            .Order()
            .ToArray();
    }

    public static TheoryData<string> NameableAiScripts() => new(NameableScriptNames());

    [Theory]
    [MemberData(nameof(NameableAiScripts))]
    public async Task Construct_The_Way_Placement_Attaches_It(string scriptName)
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
            builder.Services
                .AddWorldServices()
                .AddSingleton<WorldServer>()
                .AddSingleton<IWorldServer>(provider => provider.GetRequiredService<WorldServer>());

            using IHost host = builder.Build();

            var manager = new ScriptManager(NullLoggerFactory.Instance);
            manager.Load();
            Type scriptType = manager.GetAiScript(scriptName)!;

            ICreature creature = Substitute.For<ICreature>();
            creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());

            // Argument-for-argument identical to AttachScript.
            object built = ActivatorUtilities.CreateInstance(
                host.Services, scriptType, creature, Substitute.For<IMapInstance>());

            Assert.IsAssignableFrom<AiScript>(built);
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    /// <summary>
    /// A building block another script constructs and chains — CreatureRangeDetectorScript, built by
    /// AggroDefendScript with its own aggro range — is not something seed data should be able to
    /// name. It is marked [ChainedScript] and the loader skips it, so naming it reports "not found"
    /// rather than resolving to a type that then fails to construct.
    /// </summary>
    [Fact]
    public void Not_Resolve_A_Chained_Component_By_Name()
    {
        var manager = new ScriptManager(NullLoggerFactory.Instance);
        manager.Load();

        Assert.Null(manager.GetAiScript("CreatureRangeDetectorScript"));
    }

    /// <summary>
    /// The theory above passes vacuously if discovery finds nothing, so pin that it finds the
    /// scripts seed data actually names.
    /// </summary>
    [Fact]
    public void Discover_The_Scripts_Seed_Data_Names()
    {
        string[] names = NameableScriptNames();

        Assert.Contains("TownNpcScript", names);
        Assert.Contains("AggroDefendScript", names);
        Assert.Contains("CreaturePatrolScript", names);
    }
}
