using System.Net;
using System.Net.Sockets;
using Avalon.Combat;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Database.World.Seeding;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Parties;
using Avalon.World.Quests;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Avalon.World.Pvp;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;
using Avalon.World.Maintenance;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.World.Persistence;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// The barrier only bounds anything because the tick sweeps it. A policy nothing calls releases
/// nobody, and every login is on that path until a client sends CMSG_CHARACTER_LOADED. The same
/// holds for the per-tick inventory update: the flusher only reaches a client because the tick
/// calls it.
/// </summary>
public class WorldServerBarrierTickShould : IDisposable
{
    private readonly TcpClient _clientSide;
    private readonly TcpClient _serverSide;

    public WorldServerBarrierTickShould()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _clientSide = new TcpClient();
        _clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        _serverSide = listener.AcceptTcpClient();
        listener.Stop();
    }

    public void Dispose()
    {
        _clientSide.Dispose();
        _serverSide.Dispose();
    }

    [Fact]
    public void Spawn_a_character_whose_client_never_reported_in()
    {
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build();
        IMapInstance instance = Substitute.For<IMapInstance>();
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), instance,
            DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(16).Ticks);

        server.Tick();

        world.Received(1).SpawnInInstance(connection, instance);
    }

    [Fact]
    public async Task Timeout_release_checks_maintenance_once_and_never_spawns_a_refused_character()
    {
        var gate = Substitute.For<IWorldEntryGate>();
        gate.CheckAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(default(WorldEntryDecision));
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build(gate: gate);
        connection.AccountId = new AccountId(42);
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), Substitute.For<IMapInstance>(),
            DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(16).Ticks);

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!connection.IsClosing && DateTime.UtcNow < deadline)
        {
            server.Tick();
            await Task.Delay(10);
        }

        Assert.True(connection.IsClosing);
        world.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
        await gate.Received(1).CheckAsync(new AccountId(42), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Timeout_release_spawns_after_the_entry_check_allows_it()
    {
        var gate = Substitute.For<IWorldEntryGate>();
        gate.CheckAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntryDecision(true, DateTime.MaxValue));
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build(gate: gate);
        connection.AccountId = new AccountId(42);
        IMapInstance instance = Substitute.For<IMapInstance>();
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), instance,
            DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(16).Ticks);

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (connection.PendingSpawn is not null && DateTime.UtcNow < deadline)
        {
            server.Tick();
            await Task.Delay(10);
        }

        Assert.Null(connection.PendingSpawn);
        world.Received(1).SpawnInInstance(connection, instance);
        await gate.Received(1).CheckAsync(new AccountId(42), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Do_not_spawn_when_the_deadline_applies_after_a_completed_entry_check()
    {
        var gate = Substitute.For<IWorldEntryGate>();
        var check = new TaskCompletionSource<WorldEntryDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.CheckAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(check.Task);
        var coordinator = new WorldMaintenanceCoordinator(new WorldId(1),
            Substitute.For<IWorldMaintenanceRepository>(), Substitute.For<ICharacterSaver>(),
            TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance);
        coordinator.ApplyCommitted(new WorldMaintenanceState(false, 1, null));
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build(gate: gate, coordinator: coordinator);
        connection.AccountId = new AccountId(42);
        connection.CryptoSession.Initialize(new CryptoManager().GetPublicKey());
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), Substitute.For<IMapInstance>(),
            DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(16).Ticks);

        server.Tick();
        Assert.True(SpinWait.SpinUntil(() => gate.ReceivedCalls().Any(c =>
            c.GetMethodInfo().Name == nameof(IWorldEntryGate.CheckAsync)), TimeSpan.FromSeconds(5)));
        check.SetResult(new WorldEntryDecision(true, DateTime.MaxValue));
        await Task.Delay(100);
        coordinator.ApplyCommitted(new WorldMaintenanceState(true, 2, DateTime.UtcNow));

        server.Tick();

        Assert.True(connection.IsClosing);
        world.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
    }

    [Fact]
    public async Task Spawn_during_countdown_after_a_completed_entry_check()
    {
        var gate = Substitute.For<IWorldEntryGate>();
        gate.CheckAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntryDecision(true, DateTime.UtcNow.AddMinutes(1)));
        var coordinator = new WorldMaintenanceCoordinator(new WorldId(1),
            Substitute.For<IWorldMaintenanceRepository>(), Substitute.For<ICharacterSaver>(),
            TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance);
        coordinator.ApplyCommitted(new WorldMaintenanceState(true, 2, DateTime.UtcNow.AddMinutes(1)));
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build(gate: gate, coordinator: coordinator);
        connection.AccountId = new AccountId(42);
        IMapInstance instance = Substitute.For<IMapInstance>();
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), instance,
            DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(16).Ticks);

        DateTime timeout = DateTime.UtcNow.AddSeconds(5);
        while (connection.PendingSpawn is not null && DateTime.UtcNow < timeout)
        {
            server.Tick();
            await Task.Delay(10);
        }

        Assert.Null(connection.PendingSpawn);
        Assert.False(connection.IsClosing);
        world.Received(1).SpawnInInstance(connection, instance);
    }

    [Fact]
    public void Apply_an_offered_maintenance_cutoff_on_the_tick_and_close_non_Admins_there()
    {
        // What the Redis notification and the reconciliation do from their own threads: offer, nothing more.
        var coordinator = new WorldMaintenanceCoordinator(new WorldId(1),
            Substitute.For<IWorldMaintenanceRepository>(), Substitute.For<ICharacterSaver>(),
            TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance);
        (TestWorldServer server, _, Avalon.World.WorldConnection connection) = Build(coordinator: coordinator);
        connection.AccountId = new AccountId(42);
        var cutoff = new WorldMaintenanceState(true, 1, DateTime.UtcNow.AddSeconds(-1));
        coordinator.Offer(cutoff);

        Assert.Null(coordinator.CurrentState);
        Assert.False(connection.IsClosing);

        server.Tick();

        Assert.Equal(cutoff, coordinator.CurrentState);
        Assert.True(connection.IsClosing);
    }

    [Fact]
    public void Refuse_a_completed_entry_decision_after_its_five_second_lifetime()
    {
        var coordinator = new WorldMaintenanceCoordinator(new WorldId(1),
            Substitute.For<IWorldMaintenanceRepository>(), Substitute.For<ICharacterSaver>(),
            TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance);
        coordinator.ApplyCommitted(new WorldMaintenanceState(false, 1, null));
        bool entered = false;

        bool allowed = coordinator.RunIfEntryAllowed(Substitute.For<IWorldConnection>(),
            new WorldEntryDecision(true, DateTime.UtcNow.AddSeconds(-1)), () => entered = true);

        Assert.False(allowed);
        Assert.False(entered);
    }

    [Fact]
    public void Leave_a_character_alone_while_its_client_still_has_time()
    {
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build();
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), Substitute.For<IMapInstance>(),
            DateTime.UtcNow.Ticks);

        server.Tick();

        world.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
    }

    [Fact]
    public void Send_a_characters_inventory_changes_on_the_tick_they_were_made()
    {
        (TestWorldServer server, _, Avalon.World.WorldConnection connection) = Build();
        connection.CryptoSession.Initialize(new CryptoManager().GetPublicKey());

        CharacterEntity character = New();
        connection.Character = character;
        InventoryFor(character).TryAdd(Potion.Id, 1);

        server.Tick();

        Assert.False(character.ClientChanges.HasChanges);
    }

    /// <summary>The character sheet (#506) reaches a client only because the tick flushes it, once.</summary>
    [Fact]
    public void Send_a_characters_sheet_on_the_tick_it_is_in_the_world()
    {
        (TestWorldServer server, _, Avalon.World.WorldConnection connection) = Build();
        connection.CryptoSession.Initialize(new CryptoManager().GetPublicKey());

        CharacterEntity character = New();
        character.ApplyStats(new DerivedCharacterStats(MaxHealth: 240, MaxPower: 100, Stamina: 22, Strength: 23,
            Agility: 20, Intellect: 20, Armor: 0, BlockPct: 0f, DodgePct: 0f, CritPct: 80f, AttackDamage: 46,
            AbilityDamage: 0), CurrentValues.EnterWorld, TestCombat.Formula);
        connection.Character = character;

        server.Tick();

        Assert.Equal(CombatSeed.Formula().CritCap, character.SheetSent?.CritPct);
    }

    /// <summary>
    /// A party member status flush that throws is contained (party play, final review): the ping, outbox and continuation
    /// flushes after it still run for every connection. The party service is made to throw through its clock.
    /// </summary>
    [Fact]
    public void Run_the_rest_of_the_tick_when_the_party_status_flush_throws()
    {
        var clock = new BreakableClock { Broken = true };
        var parties = new PartyService(Options.Create(new GameConfiguration()), clock, NullLogger<PartyService>.Instance);
        (TestWorldServer server, _, Avalon.World.WorldConnection connection) = Build(parties);
        bool continued = false;
        connection.EnqueueContinuation(Task.CompletedTask, () => continued = true);

        Exception? thrown = Record.Exception(() => server.Tick());

        Assert.Null(thrown);
        Assert.True(continued);
    }

    /// <summary>
    /// #639: presence is captured by the tick, after the world update, from the registry it ticked; the Redis writer
    /// only takes what the tick handed over. A second tick within the second captures nothing new.
    /// </summary>
    [Fact]
    public void Capture_presence_on_the_tick_once_a_second()
    {
        var clock = new Avalon.Server.World.UnitTests.Loot.FixedTimeProvider(
            new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
        var presence = new Avalon.World.Presence.PresenceCapture(Options.Create(new GameConfiguration { WorldId = "4" }),
            NullLogger<Avalon.World.Presence.PresenceCapture>.Instance, clock);
        (TestWorldServer server, IWorld world, _) = Build(presence: presence);
        ICharacter nym = PendingSpawnConnection.Character();
        nym.Position.Returns(Avalon.Common.Mathematics.Vector3.zero);
        nym.Orientation.Returns(Avalon.Common.Mathematics.Vector3.zero);
        var instance = Substitute.For<IMapInstance>();
        instance.TemplateId.Returns(new Avalon.Common.ValueObjects.MapTemplateId(1));
        Dictionary<Avalon.Common.ObjectGuid, ICharacter> roster = new() { [nym.Guid] = nym };
        instance.Characters.Returns(roster);
        var registry = Substitute.For<IInstanceRegistry>();
        registry.ActiveInstances.Returns([instance]);
        world.InstanceRegistry.Returns(registry);

        server.Tick();
        Assert.Equal((ushort)4, presence.Take()?.WorldId);

        server.Tick();
        Assert.Null(presence.Take());
    }

    /// <summary>Reference data with the seeded combat formula, which the tick's sheet flush reads (#506).</summary>
    private static readonly Lazy<StaticData> SeededData = new(() => TestStaticData.LoadAsync().GetAwaiter().GetResult());

    private (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) Build(PartyService? parties = null,
        Avalon.World.Presence.PresenceCapture? presence = null, IWorldEntryGate? gate = null,
        WorldMaintenanceCoordinator? coordinator = null)
    {
        StaticData data = SeededData.Value;   // loaded outside Returns, which it would otherwise interrupt
        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration { CharacterLoadTimeoutSeconds = 15 });
        world.Data.Returns(data);

        var server = new TestWorldServer(world, parties, presence, gate, coordinator);
        var connection = new Avalon.World.WorldConnection(
            server, _clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
        server.Add(connection);
        return (server, world, connection);
    }

    /// <summary>Reaches one tick without the socket loop that normally drives it.</summary>
    private sealed class TestWorldServer : WorldServer
    {
        public TestWorldServer(IWorld world, PartyService? parties = null,
            Avalon.World.Presence.PresenceCapture? presence = null, IWorldEntryGate? gate = null,
            WorldMaintenanceCoordinator? coordinator = null) : base(
            Substitute.For<IPacketManager>(),
            NullLoggerFactory.Instance,
            new AnyServiceProvider(presence),
            Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = 0 }),
            world,
            Substitute.For<IScriptManager>(),
            Substitute.For<IReplicatedCache>(),
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<Avalon.World.Persistence.ICharacterSaver>(),
            parties ?? new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System, NullLogger<PartyService>.Instance),
            gate, coordinator)
        { }

        public void Add(Avalon.World.WorldConnection connection) => AddConnection(connection);

        public void Tick() => Update(TimeSpan.FromMilliseconds(16), 0);
    }

    /// <summary>
    /// The world server reflects over every packet handler in the assembly and activates each one,
    /// so standing it up needs a container that answers for all of their dependencies.
    /// </summary>
    private sealed class AnyServiceProvider(Avalon.World.Presence.PresenceCapture? presence = null) : IServiceProvider
    {
        private QuestService? _quests;
        private Avalon.World.Items.ItemUseService? _itemUses;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ILoggerFactory)) return NullLoggerFactory.Instance;

            // The presence capture (#639), only where a test hands one over; production registers it.
            if (serviceType == typeof(Avalon.World.Presence.PresenceCapture)) return presence;

            // PvpToggleHandler takes the one PvP toggle (#164), a class with settings and a clock.
            if (serviceType == typeof(PvpToggle))
                return new PvpToggle(Options.Create(new GameConfiguration()), TimeProvider.System);

            // The party handlers (2026-09-30) take the one party service, a class with settings, a clock and a logger.
            // The chat handler (#722) takes the one chat limiter, a class with settings and a clock; off, as the defaults are.
            if (serviceType == typeof(Avalon.World.Chat.ChatRateLimiter))
                return new Avalon.World.Chat.ChatRateLimiter(Options.Create(new GameConfiguration()), TimeProvider.System);

            if (serviceType == typeof(PartyService))
                return new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System,
                    NullLogger<PartyService>.Instance);

            // The quest handlers (#433) and the world server take the one quest service, as production's singleton.
            if (serviceType == typeof(QuestService))
                return _quests ??= Avalon.Server.World.UnitTests.Quests.InertQuestService.Create();

            // ItemUseHandler takes the one item use service, as production's singleton.
            if (serviceType == typeof(Avalon.World.Items.ItemUseService))
                return _itemUses ??= Avalon.Server.World.UnitTests.ItemUse.InertItemUseService.Create();

            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ILogger<>))
                return Activator.CreateInstance(
                    typeof(NullLogger<>).MakeGenericType(serviceType.GenericTypeArguments[0]));

            if (serviceType.IsInterface || serviceType.IsAbstract)
                return Substitute.For([serviceType], []);

            return serviceType.GetConstructor(Type.EmptyTypes) is not null
                ? Activator.CreateInstance(serviceType)
                : null;
        }
    }
}
