using System.Net;
using System.Net.Sockets;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Auth;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Maintenance;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Pvp;
using Avalon.World.Quests;
using Avalon.World.Reload;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// The world server's port opens only once the server can serve what arrives on it (#665): scripts
/// and the world loaded, the cache subscribed, the connection listener registered and the tick
/// running. Before, <c>ServerBase.StartAsync</c> listened at host start, and clients were accepted
/// while <c>World.LoadAsync</c> was still running, or after it had failed.
/// </summary>
public class WorldServerStartupShould
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private readonly int _port = FreePort();
    private readonly IWorld _world = Substitute.For<IWorld>();
    private readonly TaskCompletionSource _load = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WorldServerStartupShould()
    {
        _world.LoadAsync(Arg.Any<CancellationToken>()).Returns(_load.Task);
        _world.Configuration.Returns(new GameConfiguration());
        // The server subscribes to its own world's reload channel, so it needs to know its id.
        _world.Id.Returns(new Avalon.Domain.Auth.WorldId(1));
    }

    [Fact]
    public async Task Refuse_connections_until_the_world_has_loaded()
    {
        var server = new TestWorldServer(_world, _port);
        await server.StartAsync(CancellationToken.None);
        try
        {
            Assert.False(await AcceptsAsync(), "a client was accepted while the world was still loading");

            _load.SetResult();

            await UntilAsync(AcceptsAsync, "the port did not open once the world had loaded");
        }
        finally
        {
            await server.StopAsync(CancellationToken.None).WaitAsync(Limit);
        }
    }

    /// <summary>The API publishes reload requests on the world's own channel; subscribing elsewhere is silent.</summary>
    [Fact]
    public async Task Subscribe_to_its_own_reload_channel_once_loaded()
    {
        var cache = Substitute.For<IReplicatedCache>();
        Action<StackExchange.Redis.RedisChannel, StackExchange.Redis.RedisValue>? onReload = null;
        cache.SubscribeAsync(CacheKeys.WorldReloadChannel(1), Arg.Any<Action<StackExchange.Redis.RedisChannel, StackExchange.Redis.RedisValue>>())
            .Returns(call =>
            {
                onReload = call.ArgAt<Action<StackExchange.Redis.RedisChannel, StackExchange.Redis.RedisValue>>(1);
                return Task.CompletedTask;
            });
        var server = new TestWorldServer(_world, _port, cache);
        await server.StartAsync(CancellationToken.None);
        try
        {
            _load.SetResult();
            await UntilAsync(AcceptsAsync, "the port did not open once the world had loaded");

            Assert.NotNull(onReload);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None).WaitAsync(Limit);
        }
    }

    /// <summary>The admin app's dropdown reads what this publishes; a world that never does leaves it unchecked.</summary>
    [Fact]
    public async Task Publish_its_script_catalog_once_loaded_and_again_after_a_hot_reload()
    {
        var cache = Substitute.For<IReplicatedCache>();
        var scripts = Substitute.For<IScriptManager>();
        var hotReloader = Substitute.For<IScriptHotReloader>();
        var server = new TestWorldServer(_world, _port, cache, scripts, hotReloader);
        await server.StartAsync(CancellationToken.None);
        try
        {
            _load.SetResult();
            await UntilAsync(AcceptsAsync, "the port did not open once the world had loaded");
            await UntilAsync(() => Task.FromResult(CatalogWrites(cache) == 1), "the catalog was not published after load");
            scripts.Received(1).Load();

            List<Type> types = [typeof(Avalon.Server.World.UnitTests.Scripts.ThrowOnLeaveScript)];
            hotReloader.ScriptsHotReloaded += Raise.Event<ScriptsHotReloadedEventHandler>(types);

            await UntilAsync(() => Task.FromResult(CatalogWrites(cache) == 2), "the catalog was not published after a hot reload");
            scripts.Received(1).RegisterHotReloaded(types);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None).WaitAsync(Limit);
        }
    }

    private static int CatalogWrites(IReplicatedCache cache) => cache.ReceivedCalls().Count(c =>
        c.GetMethodInfo().Name == nameof(IReplicatedCache.SetAsync) &&
        (string)c.GetArguments()[0]! == CacheKeys.WorldScriptCatalog(1));

    [Fact]
    public async Task Never_open_the_port_when_the_world_fails_to_load()
    {
        var server = new TestWorldServer(_world, _port);
        await server.StartAsync(CancellationToken.None);
        try
        {
            _load.SetException(new InvalidOperationException("simulated load failure"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => server.ExecuteTask!.WaitAsync(Limit));
            Assert.False(await AcceptsAsync(), "a client was accepted after the world failed to load");
        }
        finally
        {
            await server.StopAsync(CancellationToken.None).WaitAsync(Limit);
        }
    }

    /// <summary>The persisted maintenance state is loaded after the world and before the port opens, so a world
    /// restarted past its deadline never admits anyone on a state it has not read.</summary>
    [Fact]
    public async Task Load_persisted_maintenance_before_opening_the_port()
    {
        var read = new TaskCompletionSource<WorldMaintenanceState?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = Substitute.For<IWorldMaintenanceRepository>();
        repository.ReadAsync(new Avalon.Domain.Auth.WorldId(1), Arg.Any<CancellationToken>()).Returns(read.Task);
        var coordinator = new WorldMaintenanceCoordinator(new Avalon.Domain.Auth.WorldId(1), repository,
            Substitute.For<ICharacterSaver>(), TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance,
            Microsoft.Extensions.Options.Options.Create(new Avalon.World.Configuration.WorldShutdownConfiguration()));
        var server = new TestWorldServer(_world, _port, maintenance: coordinator);
        await server.StartAsync(CancellationToken.None);
        try
        {
            _load.SetResult();
            await UntilAsync(() => Task.FromResult(repository.ReceivedCalls().Any()), "the maintenance state was not read");
            Assert.False(await AcceptsAsync(), "a client was accepted before the maintenance state was loaded");

            var state = new WorldMaintenanceState(true, 4, DateTime.UtcNow.AddMinutes(-1));
            read.SetResult(state);

            await UntilAsync(AcceptsAsync, "the port did not open once the maintenance state was loaded");
            Assert.Equal(state, coordinator.CurrentState);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None).WaitAsync(Limit);
        }
    }

    [Fact]
    public async Task Never_open_the_port_when_the_maintenance_state_cannot_be_read()
    {
        var repository = Substitute.For<IWorldMaintenanceRepository>();
        repository.ReadAsync(new Avalon.Domain.Auth.WorldId(1), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorldMaintenanceState?>(null));
        var coordinator = new WorldMaintenanceCoordinator(new Avalon.Domain.Auth.WorldId(1), repository,
            Substitute.For<ICharacterSaver>(), TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance,
            Microsoft.Extensions.Options.Options.Create(new Avalon.World.Configuration.WorldShutdownConfiguration()));
        var server = new TestWorldServer(_world, _port, maintenance: coordinator);
        await server.StartAsync(CancellationToken.None);
        try
        {
            _load.SetResult();

            await Assert.ThrowsAsync<InvalidOperationException>(() => server.ExecuteTask!.WaitAsync(Limit));
            Assert.False(await AcceptsAsync(), "a client was accepted without a maintenance state");
        }
        finally
        {
            await server.StopAsync(CancellationToken.None).WaitAsync(Limit);
        }
    }

    /// <summary>
    /// The restart drain (#768) runs its countdown and cutoff on the tick (#639), so a stop keeps the tick running
    /// until the drain ends: here, once the one non-Admin player left is an Admin. Only then does the stop go on.
    /// </summary>
    [Fact]
    public async Task Keep_ticking_through_a_restart_drain_until_no_non_Admin_player_is_left()
    {
        var repository = Substitute.For<IWorldMaintenanceRepository>();
        repository.ReadAsync(new Avalon.Domain.Auth.WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(false, 1, null));
        var coordinator = new WorldMaintenanceCoordinator(new Avalon.Domain.Auth.WorldId(1), repository,
            Substitute.For<ICharacterSaver>(), TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance,
            Options.Create(new WorldShutdownConfiguration
            { DrainTime = TimeSpan.FromMinutes(1), SaveMargin = TimeSpan.FromMinutes(1) }));
        var server = new TestWorldServer(_world, _port, maintenance: coordinator);
        await server.StartAsync(CancellationToken.None);
        _load.SetResult();
        await UntilAsync(AcceptsAsync, "the port did not open once the world had loaded");

        (TcpClient clientSide, TcpClient serverSide) = LoopbackPair();
        using (clientSide)
        using (serverSide)
        {
            var player = new Avalon.World.WorldConnection(server, clientSide, NullLoggerFactory.Instance,
                Substitute.For<IPacketReader>())
            {
                AccountId = new Avalon.Common.ValueObjects.AccountId(42),
            };
            server.Add(player);

            Task stopping = server.StopAsync(CancellationToken.None);
            await Task.Delay(200); // whatever the stop had left to do, it has had time to do it
            Assert.False(stopping.IsCompleted, "the stop did not wait for the restart drain");

            // Only the tick ends the drain this early: the stop's own wait runs to the deadline, a minute away.
            ((IAccessLevelAssignable)player).AssignAccessLevel(Avalon.Common.Accounts.AccountAccessLevel.Admin);

            await stopping.WaitAsync(Limit);
            Assert.False(player.IsConnected, "the stop after the drain did not close the Admin");
            await _world.Received(1).DeSpawnPlayerAsync(player);
        }
    }

    /// <summary>
    /// A host whose stop timeout has already run out cuts the drain short, and the close, the despawn and the save
    /// still run for every connection, Admins included.
    /// </summary>
    [Fact]
    public async Task Close_and_despawn_everyone_when_the_host_cuts_the_drain_short()
    {
        var repository = Substitute.For<IWorldMaintenanceRepository>();
        repository.ReadAsync(new Avalon.Domain.Auth.WorldId(1), Arg.Any<CancellationToken>())
            .Returns(new WorldMaintenanceState(false, 1, null));
        var coordinator = new WorldMaintenanceCoordinator(new Avalon.Domain.Auth.WorldId(1), repository,
            Substitute.For<ICharacterSaver>(), TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance,
            Options.Create(new WorldShutdownConfiguration
            { DrainTime = TimeSpan.FromMinutes(1), SaveMargin = TimeSpan.FromMinutes(1) }));
        var server = new TestWorldServer(_world, _port, maintenance: coordinator);
        await server.StartAsync(CancellationToken.None);
        _load.SetResult();
        await UntilAsync(AcceptsAsync, "the port did not open once the world had loaded");

        (TcpClient playerClient, TcpClient playerServer) = LoopbackPair();
        (TcpClient adminClient, TcpClient adminServer) = LoopbackPair();
        using (playerClient)
        using (playerServer)
        using (adminClient)
        using (adminServer)
        {
            var player = new Avalon.World.WorldConnection(server, playerClient, NullLoggerFactory.Instance,
                Substitute.For<IPacketReader>())
            { AccountId = new Avalon.Common.ValueObjects.AccountId(42) };
            var admin = new Avalon.World.WorldConnection(server, adminClient, NullLoggerFactory.Instance,
                Substitute.For<IPacketReader>())
            { AccountId = new Avalon.Common.ValueObjects.AccountId(43) };
            ((IAccessLevelAssignable)admin).AssignAccessLevel(Avalon.Common.Accounts.AccountAccessLevel.Admin);
            server.Add(player);
            server.Add(admin);

            using var timedOut = new CancellationTokenSource();
            await timedOut.CancelAsync();
            await server.StopAsync(timedOut.Token).WaitAsync(Limit);

            Assert.False(player.IsConnected, "the stop did not close the player");
            Assert.False(admin.IsConnected, "the stop did not close the Admin");
            await _world.Received(1).DeSpawnPlayerAsync(player);
            await _world.Received(1).DeSpawnPlayerAsync(admin);
        }
    }

    /// <summary>A stop that lands during the load leaves the port shut when the load then finishes.</summary>
    [Fact]
    public async Task Keep_the_port_shut_when_stopped_during_the_load()
    {
        var server = new TestWorldServer(_world, _port);
        await server.StartAsync(CancellationToken.None);

        Task stopping = server.StopAsync(CancellationToken.None);
        _load.SetResult();
        await stopping.WaitAsync(Limit);

        Assert.False(await AcceptsAsync(), "the port opened after the server had been stopped");
    }

    /// <summary>Whether a client can connect to the server's port right now.</summary>
    private async Task<bool> AcceptsAsync()
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, _port).WaitAsync(Limit);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task UntilAsync(Func<Task<bool>> done, string failure)
    {
        DateTime deadline = DateTime.UtcNow + Limit;
        while (!await done())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(10);
        }
    }

    private static (TcpClient clientSide, TcpClient serverSide) LoopbackPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        TcpClient serverSide = listener.AcceptTcpClient();
        listener.Stop();
        return (clientSide, serverSide);
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private sealed class TestWorldServer(IWorld world, int port, IReplicatedCache? cache = null,
        IScriptManager? scripts = null, IScriptHotReloader? hotReloader = null,
        WorldMaintenanceCoordinator? maintenance = null) : WorldServer(
        Substitute.For<IPacketManager>(),
        NullLoggerFactory.Instance,
        new AnyServiceProvider(scripts ??= Substitute.For<IScriptManager>(), cache ??= Substitute.For<IReplicatedCache>()),
        Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = (ushort)port }),
        world,
        scripts,
        cache,
        hotReloader ?? Substitute.For<IScriptHotReloader>(),
        new CharacterSaver(Substitute.For<ICharacterSaveRepository>(), NullLogger<CharacterSaver>.Instance),
        new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System, NullLogger<PartyService>.Instance),
        maintenanceCoordinator: maintenance)
    {
        public void Add(Avalon.World.WorldConnection connection) => AddConnection(connection);
    }

    /// <summary>
    /// The world server reflects over every packet handler in the assembly and activates each one,
    /// so standing it up needs a container that answers for all of their dependencies.
    /// </summary>
    private sealed class AnyServiceProvider(IScriptManager scripts, IReplicatedCache cache) : IServiceProvider
    {
        private QuestService? _quests;
        private Avalon.World.Items.ItemUseService? _itemUses;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(Avalon.World.GameAuth.WorldTlsTransport))
                return Avalon.Server.World.UnitTests.GameAuth.GameplayTestAdmission.TlsTransport();
            if (serviceType == typeof(ILoggerFactory)) return NullLoggerFactory.Instance;

            if (serviceType == typeof(ReloadRequestHandler))
                return new ReloadRequestHandler(Substitute.For<IReferenceDataReloader>(), Substitute.For<IReplicatedCache>(),
                    Options.Create(new GameConfiguration { WorldId = 1 }), NullLogger<ReloadRequestHandler>.Instance);

            if (serviceType == typeof(ScriptCatalogPublisher))
                return new ScriptCatalogPublisher(scripts, cache, Options.Create(new GameConfiguration { WorldId = 1 }),
                    Substitute.For<ILogger<ScriptCatalogPublisher>>());

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
                return Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(serviceType.GenericTypeArguments[0]));

            if (serviceType.IsInterface || serviceType.IsAbstract)
                return Substitute.For([serviceType], []);

            return serviceType.GetConstructor(Type.EmptyTypes) is not null
                ? Activator.CreateInstance(serviceType)
                : null;
        }
    }
}
