using Avalon.Database.World;
using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.Server.World.UnitTests.Doodads;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.Doodads;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avalon.Server.World.UnitTests.Hosting;

/// <summary>
/// Every doodad script builds the way DoodadScripts builds it: ActivatorUtilities over the narrowed QuestScriptServices,
/// no runtime arguments, through the production container. The seed names scripts by string, so nothing at compile time
/// catches a ScriptName naming no class, or a constructor wanting more.
/// </summary>
public class DoodadScriptConstructibilityShould
{
    private static async Task<IHost> ProductionHostAsync()
    {
        HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
        builder.Services.AddWorldServices();
        return builder.Build();
    }

    /// <summary>Shipped: the server's own, in Avalon.World (a test assembly's are not shipped); seeded: what the seed names.</summary>
    [Fact]
    public async Task Build_every_shipped_and_seeded_doodad_script_through_the_script_services()
    {
        string[] seeded;
        using (var database = SqliteDatabase.World())
        using (WorldDbContext context = database.CreateDbContext())
        {
            seeded = context.DoodadTemplates.AsNoTracking().ToList()
                .Where(t => t.ScriptName != null).Select(t => t.ScriptName!).Distinct().ToArray();
        }

        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            using IHost host = await ProductionHostAsync();
            IScriptManager scripts = host.Services.GetRequiredService<IScriptManager>();
            scripts.Load();

            // None is shipped before the stock scripts; from then on, the list is not empty.
            var shipped = typeof(ScriptManager).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(DoodadScript)) && !t.IsAbstract)
                .ToList();

            foreach (Type type in shipped)
            {
                Assert.Same(type, scripts.GetDoodadScript(type.Name));
                Assert.IsAssignableFrom<DoodadScript>(ActivatorUtilities.CreateInstance(new QuestScriptServices(host.Services), type));
            }

            foreach (string name in seeded)
            {
                Type? type = scripts.GetDoodadScript(name);
                Assert.NotNull(type);
                Assert.IsAssignableFrom<DoodadScript>(ActivatorUtilities.CreateInstance(new QuestScriptServices(host.Services), type));
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    /// <summary>The narrowing refuses, not a missing registration: the production container has the world.</summary>
    [Fact]
    public async Task Refuse_a_doodad_script_that_asks_for_the_world()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            using IHost host = await ProductionHostAsync();

            Assert.IsAssignableFrom<DoodadScript>(ActivatorUtilities.CreateInstance(host.Services, typeof(WorldHungryDoodadScript)));
            Assert.Throws<InvalidOperationException>(() =>
                ActivatorUtilities.CreateInstance(new QuestScriptServices(host.Services), typeof(WorldHungryDoodadScript)));
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }
}
