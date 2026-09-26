using System.Reflection;
using Avalon.Database.World.Repositories;
using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.Extensions;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.Server.World.UnitTests.Vendors;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Loot;
using Avalon.World.Quests;
using Avalon.World.Vendors;
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

            // Vendors (#432). Both are optional where they are consumed (World, StaticData,
            // DialogueChooseHandler, MapInstance), so only this proves production supplies them.
            Assert.NotNull(host.Services.GetRequiredService<IVendorStockRepository>());
            Assert.IsType<NoQuestProgress>(host.Services.GetRequiredService<IQuestProgress>());
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
    [InlineData(NetworkPacketType.CMSG_VENDOR_BUY, typeof(VendorBuyHandler))]
    [InlineData(NetworkPacketType.CMSG_VENDOR_SELL, typeof(VendorSellHandler))]
    [InlineData(NetworkPacketType.CMSG_VENDOR_BUYBACK, typeof(VendorBuybackHandler))]
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

    /// <summary>
    /// The restock timer a sale starts and the vendor pass that restocks it read one clock (#432):
    /// the container's TimeProvider, which MapInstance reads with GetService and the buy handler
    /// takes by injection. With that clock ten years ahead, a sale timed by any other clock would
    /// already be due at the first pass.
    /// </summary>
    [Fact]
    public async Task Take_vendor_sales_at_the_container_clock()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            var clock = new FixedTimeProvider(new DateTimeOffset(VendorTestData.Now).AddYears(10));
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddWorldServices();
            using IHost host = builder.Build();

            // What MapInstance's vendor pass restocks by.
            Assert.Same(clock, host.Services.GetRequiredService<TimeProvider>());

            VendorWorld w = await VendorWorld.CreateAsync();
            w.Clock.Now = clock.Now;
            w.OpenShop();

            // Built the way WorldServer builds it; only the world and its economy are the fixture's.
            var handler = (VendorBuyHandler)ActivatorUtilities.CreateInstance(
                host.Services, typeof(VendorBuyHandler), w.World, w.Economy);
            handler.Execute(w.Main.Connection,
                new CVendorBuyPacket { RequestId = 1, Sequence = VendorTestData.BladeSequence });
            Assert.Equal(VendorResult.Ok, w.Main.Results()[^1].Result);

            Assert.True(w.Stocks.TryGet(VendorWorld.SmithGuid, out VendorStockState? stock));
            VendorStockView blade = stock.Rows.Single(r => r.Sequence == VendorTestData.BladeSequence);

            w.Clock.Now = clock.Now.AddSeconds(59);
            w.EndOfTick();
            Assert.Equal(1u, stock.Available(blade));

            w.Clock.Now = clock.Now.AddSeconds(60);
            w.EndOfTick();
            Assert.Equal(2u, stock.Available(blade));
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }
}
