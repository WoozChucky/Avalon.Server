using Avalon.Database.World;
using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.ItemUse;
using Avalon.World.Items;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avalon.Server.World.UnitTests.Hosting;

/// <summary>
/// Every item script builds the way ItemUseService builds it: ActivatorUtilities over the narrowed
/// QuestScriptServices, no runtime arguments, through the production container. The seed names scripts by string, so
/// nothing at compile time catches a UseScript naming no class, or a constructor wanting more.
/// </summary>
public class ItemScriptConstructibilityShould
{
    private static async Task<IHost> ProductionHostAsync()
    {
        HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
        builder.Services.AddWorldServices();
        return builder.Build();
    }

    [Fact]
    public async Task Build_every_item_script_the_seed_names()
    {
        string[] named;
        using (var database = SqliteDatabase.World())
        using (WorldDbContext context = database.CreateDbContext())
        {
            named = context.ItemTemplates.AsNoTracking().ToList()
                .Where(i => i.UseScript != null).Select(i => i.UseScript!).Distinct().ToArray();
        }

        Assert.Equal(["RestoreHealth", "RestorePower", "TownPortalScroll"], named.Order(StringComparer.Ordinal));

        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            using IHost host = await ProductionHostAsync();
            IScriptManager scripts = host.Services.GetRequiredService<IScriptManager>();
            scripts.Load();

            foreach (string name in named)
            {
                Type? type = scripts.GetItemScript(name);
                Assert.NotNull(type);
                Assert.IsAssignableFrom<ItemScript>(ActivatorUtilities.CreateInstance(new QuestScriptServices(host.Services), type!));
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    [Fact]
    public async Task Build_every_item_script_the_server_ships()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            using IHost host = await ProductionHostAsync();
            IScriptManager scripts = host.Services.GetRequiredService<IScriptManager>();
            scripts.Load();

            var shipped = typeof(ScriptManager).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(ItemScript)) && !t.IsAbstract)
                .ToList();
            Assert.NotEmpty(shipped);

            foreach (Type type in shipped)
            {
                Assert.Same(type, scripts.GetItemScript(type.Name));
                Assert.IsAssignableFrom<ItemScript>(ActivatorUtilities.CreateInstance(new QuestScriptServices(host.Services), type));
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    /// <summary>The narrowing refuses, not a missing registration: the production container has the world.</summary>
    [Fact]
    public async Task Not_build_an_item_script_that_asks_for_a_service_that_writes()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            using IHost host = await ProductionHostAsync();

            Assert.IsAssignableFrom<ItemScript>(ActivatorUtilities.CreateInstance(host.Services, typeof(WorldHungryItemScript)));
            Assert.Throws<InvalidOperationException>(() =>
                ActivatorUtilities.CreateInstance(new QuestScriptServices(host.Services), typeof(WorldHungryItemScript)));
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }
}
