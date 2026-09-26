using System.Reflection;
using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
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

    /// <summary>One row per in-map handler that takes more than an IWorld: each is built from the container.</summary>
    [Theory]
    [InlineData(NetworkPacketType.CMSG_LOOT_PICKUP, typeof(LootPickupHandler))]
    [InlineData(NetworkPacketType.CMSG_ITEM_MOVE, typeof(ItemMoveHandler))]
    [InlineData(NetworkPacketType.CMSG_ITEM_DESTROY, typeof(ItemDestroyHandler))]
    public async Task Find_And_Build_The_Handler_The_Way_WorldServer_Does(NetworkPacketType opcode, Type expected)
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
            builder.Services.AddWorldServices();
            using IHost host = builder.Build();

            // The same scan as the WorldServer constructor.
            Type handlerType = Assert.Single(typeof(WorldServer).Assembly.GetTypes(),
                t => t.GetCustomAttribute<PacketHandlerAttribute>()?.PacketType == opcode);
            Assert.Equal(expected, handlerType);

            Assert.IsType(expected, ActivatorUtilities.CreateInstance(host.Services, handlerType));
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }
}
