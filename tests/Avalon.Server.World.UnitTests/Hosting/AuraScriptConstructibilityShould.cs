using Avalon.Database.World;
using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.Auras;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Avalon.Server.World.UnitTests.Hosting;

/// <summary>
/// Every aura script builds the way AuraScripts builds it: ActivatorUtilities over the narrowed QuestScriptServices,
/// no runtime arguments, through the production container. The seed names scripts by string, so nothing at compile time
/// catches a ScriptName naming no class, or a constructor wanting more.
/// </summary>
public class AuraScriptConstructibilityShould
{
    private static async Task<IHost> ProductionHostAsync()
    {
        HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
        builder.Services.AddWorldServices();
        return builder.Build();
    }

    /// <summary>
    /// The seed names no aura script yet. Once one does, this fails: replace the assertion with a loop that resolves
    /// and builds each named script, as ItemScriptConstructibilityShould does for item scripts.
    /// </summary>
    [Fact]
    public void Build_every_aura_script_the_seed_names()
    {
        string[] named;
        using (SqliteDatabase<WorldDbContext> database = SqliteDatabase.World())
        using (WorldDbContext context = database.CreateDbContext())
            named = context.AuraTemplates.AsNoTracking().ToList()
                .Where(a => a.ScriptName != null).Select(a => a.ScriptName!).Distinct().ToArray();

        Assert.Empty(named);
    }

    /// <summary>The server ships none yet; every one it ever ships must build, and be found by its own name.</summary>
    [Fact]
    public async Task Build_every_aura_script_the_server_ships()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            using IHost host = await ProductionHostAsync();
            IScriptManager scripts = host.Services.GetRequiredService<IScriptManager>();
            scripts.Load();

            foreach (Type type in typeof(ScriptManager).Assembly.GetTypes()
                         .Where(t => t.IsSubclassOf(typeof(AuraScript)) && !t.IsAbstract))
            {
                Assert.Same(type, scripts.GetAuraScript(type.Name));
                Assert.IsAssignableFrom<AuraScript>(ActivatorUtilities.CreateInstance(new QuestScriptServices(host.Services), type));
            }

            // The test assembly's scripts are found too, by their own names.
            Assert.Same(typeof(RecordingAuraScript), scripts.GetAuraScript(nameof(RecordingAuraScript)));
            Assert.Contains(nameof(RecordingAuraScript), scripts.AuraScriptNames);
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    /// <summary>The narrowing refuses, not a missing registration: the production container has the world.</summary>
    [Fact]
    public async Task Not_build_an_aura_script_that_asks_for_a_service_that_writes()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            using IHost host = await ProductionHostAsync();

            Assert.IsAssignableFrom<AuraScript>(ActivatorUtilities.CreateInstance(host.Services, typeof(WorldHungryAuraScript)));
            Assert.Throws<InvalidOperationException>(() =>
                ActivatorUtilities.CreateInstance(new QuestScriptServices(host.Services), typeof(WorldHungryAuraScript)));

            AuraScripts auraScripts = host.Services.GetRequiredService<AuraScripts>();
            host.Services.GetRequiredService<IScriptManager>().Load();
            Assert.Null(auraScripts.For(new Avalon.Domain.World.AuraTemplate
            { Id = new Avalon.Common.ValueObjects.AuraId(1), ScriptName = nameof(WorldHungryAuraScript) }));
            Assert.IsType<RecordingAuraScript>(auraScripts.For(new Avalon.Domain.World.AuraTemplate
            { Id = new Avalon.Common.ValueObjects.AuraId(2), ScriptName = nameof(RecordingAuraScript) }));
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }
}
