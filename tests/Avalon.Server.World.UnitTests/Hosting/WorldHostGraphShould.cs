using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Loot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Avalon.Server.World.UnitTests.Hosting;

/// <summary>
/// The world host composed exactly as its entry point composes it, built under the validation
/// every Avalon host now enables. Before repositories took a context factory this threw: the
/// singletons that inject a repository — the creature spawner, the placement service — were
/// capturing a scoped <c>DbContext</c> for the life of the process.
/// </summary>
public class WorldHostGraphShould
{
    [Fact]
    public async Task Build_with_no_captured_scoped_services()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
            builder.Services
                .AddWorldServices()
                .AddSingleton<WorldServer>()
                .AddSingleton<IWorldServer>(provider => provider.GetRequiredService<WorldServer>())
                .AddHostedService(provider => provider.GetRequiredService<WorldServer>());

            using IHost host = builder.Build();

            Assert.NotNull(host);

            // CreatureSpawner now depends on IWorld directly. World's own constructor does not
            // depend back on ICreatureSpawner, so resolving this must not throw for a cycle.
            Assert.NotNull(host.Services.GetRequiredService<ICreatureSpawner>());

            // Loot (#460). MapInstance reads these with GetService, so a missing registration would
            // not fail anything else: it would silently make every creature drop nothing.
            Assert.NotNull(host.Services.GetRequiredService<ILootRoller>());
            Assert.NotNull(host.Services.GetRequiredService<ILootAllocator>());
            Assert.NotNull(host.Services.GetRequiredService<TimeProvider>());
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }
}
