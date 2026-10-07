using Avalon.Database.World;
using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Quests;
using Avalon.World.Public.Scripts;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avalon.Server.World.UnitTests.Hosting;

/// <summary>
/// Every quest script builds from DI alone through the production container, exactly as QuestService builds it
/// (ActivatorUtilities over the narrowed QuestScriptServices, no runtime arguments, #433, #738). Seed data names scripts by string, so nothing at compile time
/// can catch a constructor that wants something else; QuestService would log it and refuse the quest to everyone.
/// </summary>
public class QuestScriptConstructibilityShould
{
    private static async Task<IHost> ProductionHostAsync()
    {
        HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
        builder.Services.AddWorldServices();
        return builder.Build();
    }

    [Fact]
    public async Task Build_every_quest_script_the_server_ships_and_the_sample_one()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            using IHost host = await ProductionHostAsync();
            IScriptManager scripts = host.Services.GetRequiredService<IScriptManager>();
            scripts.Load();

            // The server's scripts live in Avalon.World, as AiScriptConstructibilityShould reads them. ScriptManager also
            // finds the test assembly's scripts, but a test's deliberately unbuildable one is not the server's, and
            // walking every loaded assembly's GetTypes() can throw ReflectionTypeLoadException, which ScriptManager
            // catches and this test would not.
            var shipped = typeof(ScriptManager).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(QuestScript)) && !t.IsAbstract)
                .Append(typeof(SampleQuestScript))
                .ToList();

            foreach (Type type in shipped)
            {
                Assert.Same(type, scripts.GetQuestScript(type.Name));
                Assert.IsAssignableFrom<QuestScript>(ActivatorUtilities.CreateInstance(new QuestScriptServices(host.Services), type));
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    /// <summary>
    /// #738: the narrowing is what refuses, not a missing registration. The production container has the quest service
    /// and the world, so a script asking for either builds from it directly, and fails through QuestScriptServices.
    /// </summary>
    [Theory]
    [InlineData(typeof(QuestServiceHungryScript))]
    [InlineData(typeof(WorldHungryScript))]
    [InlineData(typeof(ProviderHungryScript))]
    public async Task Not_build_a_script_that_asks_for_a_service_that_writes(Type type)
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            using IHost host = await ProductionHostAsync();

            Assert.IsAssignableFrom<QuestScript>(ActivatorUtilities.CreateInstance(host.Services, type));
            Assert.Throws<InvalidOperationException>(() => ActivatorUtilities.CreateInstance(new QuestScriptServices(host.Services), type));
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    public sealed class QuestServiceHungryScript(QuestService quests) : QuestScript
    {
        public QuestService Quests { get; } = quests;
    }

    public sealed class WorldHungryScript(Avalon.World.IWorld world) : QuestScript
    {
        public Avalon.World.IWorld World { get; } = world;
    }

    /// <summary>The provider itself would hand over everything.</summary>
    public sealed class ProviderHungryScript(IServiceProvider services) : QuestScript
    {
        public IServiceProvider Services { get; } = services;
    }

    /// <summary>
    /// Every script name the seed gives a quest resolves to a script that builds. The seed names none yet (spec §5),
    /// so this passes empty until one does; it is the guard for that day.
    /// </summary>
    [Fact]
    public async Task Build_every_quest_script_the_seed_names()
    {
        string[] named;
        using (var database = SqliteDatabase.World())
        using (WorldDbContext context = database.CreateDbContext())
            named = context.QuestTemplates.AsNoTracking().Where(q => q.ScriptName != null).Select(q => q.ScriptName!).Distinct().ToArray();

        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            using IHost host = await ProductionHostAsync();
            IScriptManager scripts = host.Services.GetRequiredService<IScriptManager>();
            scripts.Load();

            foreach (string name in named)
            {
                Type? type = scripts.GetQuestScript(name);
                Assert.NotNull(type);
                Assert.IsAssignableFrom<QuestScript>(ActivatorUtilities.CreateInstance(new QuestScriptServices(host.Services), type!));
            }
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }
}
