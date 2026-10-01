using System.Reflection;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.Extensions;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.Server.World.UnitTests.Vendors;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Loot;
using Avalon.World.Parties;
using Avalon.World.Public.Combat;
using Avalon.World.Pvp;
using Avalon.World.Quests;
using Avalon.World.Vendors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
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
            Assert.IsType<PartyLootAllocator>(host.Services.GetRequiredService<ILootAllocator>());
            Assert.NotNull(host.Services.GetRequiredService<TimeProvider>());

            // Vendors (#432). Both are optional where they are consumed (World, StaticData,
            // DialogueChooseHandler, MapInstance), so only this proves production supplies them.
            Assert.NotNull(host.Services.GetRequiredService<IVendorStockRepository>());
            Assert.NotNull(host.Services.GetRequiredService<ICombatDataRepository>());

            // Quests (#433). Production registers the quest repository; that CharacterSelectHandler is handed it (it
            // takes it optionally and loads an empty log without it) is Hand_the_quest_service_to_the_handlers.
            Assert.NotNull(host.Services.GetRequiredService<ICharacterQuestRepository>());

            // #506. MapInstance reads the combat random with GetService and otherwise falls back to one
            // that never crits, dodges or blocks, so only this proves production rolls for real.
            Assert.IsType<Avalon.Combat.CombatRandom>(host.Services.GetRequiredService<Avalon.Combat.ICombatRandom>());
            Assert.IsType<QuestProgress>(host.Services.GetRequiredService<IQuestProgress>());
            // #433. MapInstance and WorldServer read it with GetService, so only this proves production registers it.
            Assert.NotNull(host.Services.GetRequiredService<QuestService>());

            // PvP (#164). MapInstance reads the toggle with GetService, so a missing registration would
            // silently build a second toggle over a different clock. /pvp is found through ICommand.
            Assert.NotNull(host.Services.GetRequiredService<PvpToggle>());
            Assert.Contains(host.Services.GetServices<ICommand>(), c => c is PvpCommand);

            // Parties (2026-09-30). World, MapInstance and EnterMapHandler take it optionally (WorldServer requires it,
            // pinned below), so only this proves production supplies one.
            Assert.NotNull(host.Services.GetRequiredService<Avalon.World.Parties.PartyService>());
            // World resolves the town return lazily (it depends on World) when a party leave countdown runs out, so only
            // this proves production registers it.
            Assert.NotNull(host.Services.GetRequiredService<Avalon.World.Respawn.TownReturn>());
            Assert.Contains(host.Services.GetServices<ICommand>(), c => c is GodModeCommand);
            Assert.Contains(host.Services.GetServices<ICommand>(), c => c is PartyChatCommand);
            Assert.Contains(host.Services.GetServices<ICommand>(), c => c is InviteCommand);
            // Whispers (#717). PartyService takes the online lookup optionally and would otherwise keep a private one the
            // whisper never sees, so only this proves both read the one production registers.
            Assert.Contains(host.Services.GetServices<ICommand>(), c => c is WhisperCommand);
            Assert.Same(host.Services.GetRequiredService<Avalon.World.Characters.OnlineCharacters>(),
                host.Services.GetRequiredService<PartyService>().Online);

            // CombatConfig is still one singleton: CastAbilityHandler reads its global cooldown, and
            // every combat service reads the same values. The facing cone it once carried is gone
            // (#164), and CharacterSelectHandler no longer takes it, but must still build from here.
            ServiceDescriptor combatConfig = Assert.Single(builder.Services,
                d => d.ServiceType == typeof(CombatConfig));
            Assert.Equal(ServiceLifetime.Singleton, combatConfig.Lifetime);
            Assert.NotNull(ActivatorUtilities.CreateInstance<CharacterSelectHandler>(host.Services));
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    /// <summary>
    /// The member status flush runs from WorldServer.Update (2026-09-30), so the world server must get the same
    /// party service the handlers do: one singleton, and a constructor parameter with no default to fall back on.
    /// </summary>
    [Fact]
    public async Task Give_the_world_server_the_one_party_service()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
            builder.Services.AddWorldServices();

            ServiceDescriptor parties = Assert.Single(builder.Services, d => d.ServiceType == typeof(PartyService));
            Assert.Equal(ServiceLifetime.Singleton, parties.Lifetime);

            ParameterInfo parameter = Assert.Single(Assert.Single(typeof(WorldServer).GetConstructors()).GetParameters(),
                p => p.ParameterType == typeof(PartyService));
            Assert.False(parameter.HasDefaultValue);
            Assert.False(new NullabilityInfoContext().Create(parameter).WriteState is NullabilityState.Nullable);

            using IHost host = builder.Build();
            Assert.Same(host.Services.GetRequiredService<PartyService>(), host.Services.GetRequiredService<PartyService>());
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    /// <summary>One row per in-map handler that takes more than an IWorld: each is built from the container.</summary>
    [Theory]
    [InlineData(NetworkPacketType.CMSG_CAST_ABILITY, typeof(CastAbilityHandler))]
    [InlineData(NetworkPacketType.CMSG_LOOT_PICKUP, typeof(LootPickupHandler))]
    [InlineData(NetworkPacketType.CMSG_ITEM_MOVE, typeof(ItemMoveHandler))]
    [InlineData(NetworkPacketType.CMSG_ITEM_DESTROY, typeof(ItemDestroyHandler))]
    [InlineData(NetworkPacketType.CMSG_VENDOR_BUY, typeof(VendorBuyHandler))]
    [InlineData(NetworkPacketType.CMSG_VENDOR_SELL, typeof(VendorSellHandler))]
    [InlineData(NetworkPacketType.CMSG_VENDOR_BUYBACK, typeof(VendorBuybackHandler))]
    [InlineData(NetworkPacketType.CMSG_PVP_TOGGLE, typeof(PvpToggleHandler))]
    [InlineData(NetworkPacketType.CMSG_CHAT_MESSAGE, typeof(ChatMessageHandler))]
    [InlineData(NetworkPacketType.CMSG_PARTY_INVITE, typeof(PartyInviteHandler))]
    [InlineData(NetworkPacketType.CMSG_PARTY_INVITE_RESPONSE, typeof(PartyInviteResponseHandler))]
    [InlineData(NetworkPacketType.CMSG_PARTY_LEAVE, typeof(PartyLeaveHandler))]
    [InlineData(NetworkPacketType.CMSG_PARTY_KICK, typeof(PartyKickHandler))]
    [InlineData(NetworkPacketType.CMSG_PARTY_PROMOTE, typeof(PartyPromoteHandler))]
    [InlineData(NetworkPacketType.CMSG_PARTY_EXPERIENCE_MODE, typeof(PartyExperienceModeHandler))]
    [InlineData(NetworkPacketType.CMSG_QUEST_ACCEPT, typeof(QuestAcceptHandler))]
    [InlineData(NetworkPacketType.CMSG_QUEST_TURN_IN, typeof(QuestTurnInHandler))]
    [InlineData(NetworkPacketType.CMSG_QUEST_ABANDON, typeof(QuestAbandonHandler))]
    [InlineData(NetworkPacketType.CMSG_ENTER_MAP, typeof(EnterMapHandler))]
    [InlineData(NetworkPacketType.CMSG_INTERACT, typeof(InteractHandler))]
    [InlineData(NetworkPacketType.CMSG_DIALOGUE_CHOOSE, typeof(DialogueChooseHandler))]
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
    /// Quests (#433): these handlers take the quest service, and some the quest repository or the quest progress, as
    /// optional constructor parameters, so a handler the container builds without them would silently go without.
    /// Without the repository CharacterSelectHandler spawns every character with an empty log, its completed quests
    /// included, so each could turn the storyline in again for its rewards every session; without the service no log
    /// is loaded, InteractHandler and DialogueChooseHandler offer no quests and credit no talk, and LootPickupHandler
    /// lets anyone pick up a quest item; without the progress DialogueChooseHandler unlocks no gated stock. Built the
    /// way WorldServer builds them (handing a handler that needs it the IWorldServer), each must hold the container's
    /// own.
    /// </summary>
    [Theory]
    [InlineData(typeof(InteractHandler))]
    [InlineData(typeof(DialogueChooseHandler))]
    [InlineData(typeof(CharacterSelectHandler))]
    [InlineData(typeof(LootPickupHandler))]
    public async Task Hand_the_quest_service_to_the_handlers(Type handlerType)
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
            builder.Services.AddWorldServices();
            using IHost host = builder.Build();

            bool needsWorldServer = handlerType.GetConstructors()
                .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IWorldServer)));
            object handler = needsWorldServer
                ? ActivatorUtilities.CreateInstance(host.Services, handlerType, Substitute.For<IWorldServer>())
                : ActivatorUtilities.CreateInstance(host.Services, handlerType);

            Assert.Same(host.Services.GetRequiredService<QuestService>(), CapturedOfType<QuestService>(handler));
            if (handlerType == typeof(DialogueChooseHandler))
                Assert.Same(host.Services.GetRequiredService<IQuestProgress>(), CapturedOfType<IQuestProgress>(handler));
            if (handlerType == typeof(CharacterSelectHandler))
                Assert.Same(host.Services.GetRequiredService<ICharacterQuestRepository>(),
                    CapturedOfType<ICharacterQuestRepository>(handler));
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    /// <summary>The one field of this type a handler holds (a primary constructor's captured parameter).</summary>
    private static T? CapturedOfType<T>(object handler) where T : class =>
        (T?)Assert.Single(handler.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            f => f.FieldType == typeof(T)).GetValue(handler);

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
