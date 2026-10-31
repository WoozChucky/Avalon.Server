using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Character.Repositories;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Generic;
using Avalon.World;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.Server.World.UnitTests.Trading;
using Avalon.World.Configuration;
using Avalon.World.GameAuth;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Pvp;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Avalon.World.Threading;
using Avalon.World.Trading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.WorldConnection;

/// <summary>
/// A character is written back to the database by its despawn, and the despawn is queued by the
/// close and run by the tick. Shutdown stops the tick before closing anything, so the pass that
/// runs them afterwards is the only thing standing between a clean restart and every logged-in
/// character left online at a stale position.
/// </summary>
public class WorldServerShutdownShould : IDisposable
{
    private readonly TcpClient _clientSide;
    private readonly TcpClient _serverSide;

    public WorldServerShutdownShould()
    {
        (_clientSide, _serverSide) = CreateLoopbackPair();
    }

    public void Dispose()
    {
        _clientSide.Dispose();
        _serverSide.Dispose();
    }

    [Fact]
    public async Task DespawnAConnectionItClosed()
    {
        IWorld world = Substitute.For<IWorld>();
        var server = new TestWorldServer(world);
        Avalon.World.WorldConnection connection = Connect(server);

        await server.Stop();

        await world.Received(1).DeSpawnPlayerAsync(connection);
    }

    /// <summary>
    /// Starting the saves is not enough: the host stops as soon as this returns, so a despawn it
    /// did not wait for is a character write racing process exit.
    /// </summary>
    [Fact]
    public async Task NotFinishStopping_UntilThoseDespawnsHave()
    {
        var despawnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishDespawn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        IWorld world = Substitute.For<IWorld>();
        world.DeSpawnPlayerAsync(Arg.Any<IWorldConnection>()).Returns(_ =>
        {
            despawnStarted.TrySetResult();
            return finishDespawn.Task;
        });

        var server = new TestWorldServer(world);
        Connect(server);

        Task stopping = server.Stop();

        await despawnStarted.Task;
        await Task.Delay(50); // whatever shutdown had left to do, it has had time to do it

        Assert.False(stopping.IsCompleted, "Shutdown returned while the despawn it started was still running");

        finishDespawn.SetResult();
        await stopping;
    }

    /// <summary>
    /// A despawn is started fire-and-forget by the tick that dequeues it. One started on an earlier
    /// tick, still queued behind another save or mid-transaction when the host stops, is not in the
    /// pass above, and returning before it commits disposes the database under it.
    /// </summary>
    [Fact]
    public async Task Wait_for_a_despawn_save_started_before_shutdown()
    {
        var limit = TimeSpan.FromSeconds(5);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int committed = 0;
        ICharacterSaveRepository repository = Substitute.For<ICharacterSaveRepository>();
        repository.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await gate.Task.WaitAsync(limit);
                Volatile.Write(ref committed, 1);
            });
        var saver = new CharacterSaver(repository, NullLogger<CharacterSaver>.Instance);
        var server = new TestWorldServer(Substitute.For<IWorld>(), saver);

        // What an earlier tick's DeSpawnPlayerAsync leaves behind: a save in the chain, not awaited.
        Task<bool> despawnSave = saver.SaveOnDespawnAsync(
            Avalon.Server.World.UnitTests.Inventory.TestCharacters.New(), prepareRow: null, CancellationToken.None);

        Task stopping = server.Stop();
        await Task.Delay(50); // whatever shutdown had left to do, it has had time to do it

        Assert.False(stopping.IsCompleted, "Shutdown returned while an earlier despawn save was still writing");

        gate.SetResult();
        await stopping.WaitAsync(limit);

        Assert.Equal(1, Volatile.Read(ref committed));
        Assert.True(await despawnSave.WaitAsync(limit));
    }

    /// <summary>
    /// One despawn that throws at once (the tick-thread assertion refusing it when the tick outlived its join, #639)
    /// costs the other characters neither their despawn nor the wait for every save.
    /// </summary>
    [Fact]
    public async Task Despawn_the_others_and_wait_for_saves_when_one_despawn_throws()
    {
        (TcpClient otherClient, TcpClient otherServer) = CreateLoopbackPair();
        using (otherClient)
        using (otherServer)
        {
            IWorld world = Substitute.For<IWorld>();
            ICharacterSaver saver = Substitute.For<ICharacterSaver>();
            saver.WhenAllIdle().Returns(Task.CompletedTask);
            var server = new TestWorldServer(world, saver);
            Avalon.World.WorldConnection first = Connect(server);
            var second = new Avalon.World.WorldConnection(
                server, otherClient, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
            server.Add(second);
            world.DeSpawnPlayerAsync(Arg.Any<IWorldConnection>()).Returns(call =>
                ReferenceEquals(call.Arg<IWorldConnection>(), first)
                    ? throw new InvalidOperationException("World.LeaveWorldAsync runs on the tick thread only")
                    : Task.CompletedTask);

            await server.Stop().WaitAsync(TimeSpan.FromSeconds(5));

            await world.Received(1).DeSpawnPlayerAsync(second);
            await saver.Received(1).WhenAllIdle();
        }
    }

    /// <summary>The wait is bounded by the host: a save that never finishes must not hold the process up forever.</summary>
    [Fact]
    public async Task Stop_waiting_for_saves_once_the_host_gives_up()
    {
        ICharacterSaver saver = Substitute.For<ICharacterSaver>();
        saver.WhenAllIdle().Returns(new TaskCompletionSource().Task);
        var server = new TestWorldServer(Substitute.For<IWorld>(), saver);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await server.Stop(cancelled.Token).WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// A trade still completing at shutdown (#1175), whose paired save never answers, settles alongside the other
    /// characters' despawn saves rather than ahead of them, and does not hold the stop: its trader's despawn save, held
    /// by the trade, and the saver's idle task, which waits on the hold, are given up at the end of the save window.
    /// </summary>
    [Fact]
    public async Task Despawn_the_others_while_a_trade_settles_and_stop_with_the_trade_unsettled()
    {
        var limit = TimeSpan.FromSeconds(30);
        (TradeTestWorld trading, _, _, _, _) = TradeCompletionShould.Confirmed();
        (TcpClient traderClient, TcpClient traderServer) = CreateLoopbackPair();
        using (traderClient)
        using (traderServer)
        {
            IWorld world = Substitute.For<IWorld>();
            ICharacterSaver saver = Substitute.For<ICharacterSaver>();
            saver.WhenAllIdle().Returns(new TaskCompletionSource().Task);
            var server = new TestWorldServer(world, saver, trades: trading.Trades) { SaveDrainLimit = TimeSpan.FromSeconds(1) };
            Avalon.World.WorldConnection other = Connect(server);
            var trader = new Avalon.World.WorldConnection(
                server, traderClient, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
            server.Add(trader);
            var otherSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            world.DeSpawnPlayerAsync(Arg.Any<IWorldConnection>()).Returns(call =>
            {
                if (!ReferenceEquals(call.Arg<IWorldConnection>(), other))
                    return new TaskCompletionSource().Task;   // the trader's despawn save, which the trade's hold keeps

                otherSaved.TrySetResult();
                return Task.CompletedTask;
            });

            Task stopping = server.Stop();

            // The settle cannot end before the trade's clock moves, so the other despawn ran while it waited.
            await otherSaved.Task.WaitAsync(limit);
            Assert.False(stopping.IsCompleted, "Shutdown returned before the trade's settle gave up");

            using var guard = new CancellationTokenSource(limit);
            while (!stopping.IsCompleted && !guard.IsCancellationRequested)
            {
                trading.Clock.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(20);
            }

            await stopping.WaitAsync(limit);
            Assert.Equal(TradeState.Completing, trading.Trades.TradeOf(1)!.State);   // unsettled: its holds are kept
            Assert.Empty(trading.Saver.Released);
        }
    }

    /// <summary>
    /// The shutdown notice goes out after the tick has stopped (#875): the send threads outlive the tick, so the close
    /// still writes it, and they stop last.
    /// </summary>
    [Fact]
    public async Task Send_the_shutdown_notice_after_the_tick_stops_and_stop_the_send_threads_last()
    {
        var server = new TestWorldServer(Substitute.For<IWorld>());
        server.SendScheduler.Start();
        Avalon.World.WorldConnection connection = Connect(server);
        var wire = new MemoryStream();
        connection.InitOutboxForTest(new PacketStream(wire));

        await server.Stop().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(server.SendScheduler.IsRunning);
        SDisconnectPacket? notice = null;
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await foreach (ReadOnlyMemory<byte> raw in new PacketStream(new MemoryStream(wire.ToArray())).EnumerateRawFramesAsync(256, int.MaxValue, guard.Token))
        {
            var frame = InboundPacketFrame.ParseFrame(raw);
            if (frame.Header.Type == NetworkPacketType.SMSG_DISCONNECT)
                notice = Serializer.Deserialize<SDisconnectPacket>(frame.Payload);
        }

        Assert.Equal(DisconnectReason.ServerShutdown, notice?.ReasonCode);
    }

    /// <summary>
    /// A stop ends every session with identity a bounded number at once (#1051): about 1,300 ends sent together took
    /// identity 3.6 s each and ran it out of memory. The ends past the bound wait their turn, none is refused, and the
    /// stop waits for all of them.
    /// </summary>
    [Fact]
    public async Task End_every_session_at_shutdown_a_bounded_number_at_once()
    {
        const int Sessions = 5;
        const int Bound = 2;
        var limit = TimeSpan.FromSeconds(30);
        var boundReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int inFlight = 0, mostInFlight = 0, ended = 0;
        IGameAdmissionClient admission = Substitute.For<IGameAdmissionClient>();
        admission.EndAsync(Arg.Any<GameSessionLease>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            int now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref mostInFlight, now);
            if (now == Bound) boundReached.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref inFlight);
            Interlocked.Increment(ref ended);
            return new SessionLeaseResponse { State = "ended" };
        });
        var work = new CountingWork(new WorldDatabaseWork(Bound, int.MaxValue), Sessions);
        var server = new TestWorldServer(Substitute.For<IWorld>(), admission: admission) { SessionEnds = work };
        var pairs = new List<(TcpClient, TcpClient)>();
        try
        {
            AdmitSessions(server, Sessions, TimeProvider.System, pairs);

            Task stopping = server.Stop();

            // Every drain has handed its end over, and the bound's worth are with identity: the rest wait.
            await work.AllSubmitted.WaitAsync(limit);
            await boundReached.Task.WaitAsync(limit);
            Assert.Equal(Bound, Volatile.Read(ref inFlight));
            Assert.False(stopping.IsCompleted, "Shutdown returned while session ends were still waiting");

            release.SetResult();
            await stopping.WaitAsync(limit);

            Assert.Equal(Sessions, Volatile.Read(ref ended));
            Assert.Equal(Bound, Volatile.Read(ref mostInFlight));
        }
        finally
        {
            release.TrySetResult();
            Dispose(pairs);
        }
    }

    /// <summary>
    /// A hung identity must not hold the stop past the pod's grace period (#1051): once the host's stop timeout has
    /// passed, the end in flight is cancelled and the ends still waiting for their turn are not sent.
    /// </summary>
    [Fact]
    public async Task Drop_the_session_ends_still_waiting_once_the_hosts_stop_timeout_passes()
    {
        const int Sessions = 3;
        var limit = TimeSpan.FromSeconds(30);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        IGameAdmissionClient admission = Substitute.For<IGameAdmissionClient>();
        admission.EndAsync(Arg.Any<GameSessionLease>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            // An identity that never answers: the request ends only when its token does, as the client's would.
            try { await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()); }
            catch (OperationCanceledException) { }
            return new SessionLeaseResponse { Error = GameAuthErrors.ServiceUnavailable };
        });
        var work = new CountingWork(new WorldDatabaseWork(1, int.MaxValue), Sessions);
        var server = new TestWorldServer(Substitute.For<IWorld>(), admission: admission) { SessionEnds = work };
        var pairs = new List<(TcpClient, TcpClient)>();
        using var hostGivesUp = new CancellationTokenSource();
        try
        {
            AdmitSessions(server, Sessions, TimeProvider.System, pairs);

            Task stopping = server.Stop(hostGivesUp.Token);
            await work.AllSubmitted.WaitAsync(limit);
            await entered.Task.WaitAsync(limit);
            await hostGivesUp.CancelAsync();

            await stopping.WaitAsync(limit);
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally { Dispose(pairs); }
    }

    /// <summary>
    /// A session whose lease ran out more than the clocks' skew ago holds nothing identity's end would release (#1051),
    /// so its end is not sent. One whose heartbeat once ended without its renewal applied (here answered
    /// <c>SERVICE_UNAVAILABLE</c>) is ended even after a later heartbeat was applied: identity may still hold the earlier
    /// request and renew the session past the lease held here. The heartbeats run through the tick's lease check.
    /// </summary>
    [Fact]
    public async Task Skip_the_end_of_a_lease_long_run_out_unless_a_heartbeats_outcome_was_unknown()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.Zero));
        var ended = new List<long>();
        int unavailableLeft = 1;
        IGameAdmissionClient admission = Substitute.For<IGameAdmissionClient>();
        admission.HeartbeatAsync(Arg.Any<GameSessionLease>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            GameSessionLease lease = call.Arg<GameSessionLease>();
            // Account 2's first heartbeat: identity's answer is an outage, so what it did with the request is unknown.
            if (lease.Authority.AccountId.Value == 2 && Interlocked.Exchange(ref unavailableLeft, 0) == 1)
                return Task.FromResult(new SessionLeaseResponse { Error = GameAuthErrors.ServiceUnavailable });
            return Task.FromResult(Renewal(lease, clock));
        });
        admission.EndAsync(Arg.Any<GameSessionLease>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            lock (ended) ended.Add(call.Arg<GameSessionLease>().Authority.AccountId.Value);
            return Task.FromResult(new SessionLeaseResponse { State = "ended" });
        });
        var server = new TestWorldServer(Substitute.For<IWorld>(), admission: admission);
        var pairs = new List<(TcpClient, TcpClient)>();
        try
        {
            List<Avalon.World.WorldConnection> connections = AdmitSessions(server, 2, clock, pairs);
            await HeartbeatAsync(connections[0], clock);   // applied
            await HeartbeatAsync(connections[1], clock);   // SERVICE_UNAVAILABLE
            await HeartbeatAsync(connections[1], clock);   // applied
            Assert.Equal(clock.GetUtcNow().UtcDateTime.AddSeconds(45), connections[1].GameSessionLease!.LeaseUntil);

            clock.Advance(TimeSpan.FromSeconds(45) + Avalon.World.WorldServer.SessionEndSkew);
            await server.Stop().WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(new[] { 2L }, ended);
        }
        finally { Dispose(pairs); }
    }

    /// <summary>
    /// #967: each session's end tells identity why it ended, which decides whether the place is held. The reason is the
    /// one the world closed the connection for, noted at the close and read once as the drain builds the end: a kick, a
    /// close the world did not choose (a drop), and the stop's own close.
    /// </summary>
    [Fact]
    public async Task Send_each_session_end_with_the_reason_its_connection_closed_for()
    {
        var reasons = new Dictionary<long, string>();
        IGameAdmissionClient admission = Substitute.For<IGameAdmissionClient>();
        admission.EndAsync(Arg.Any<GameSessionLease>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            lock (reasons) reasons.Add(call.Arg<GameSessionLease>().Authority.AccountId.Value, call.Arg<string>());
            return Task.FromResult(new SessionLeaseResponse { State = "ended" });
        });
        var server = new TestWorldServer(Substitute.For<IWorld>(), admission: admission);
        var pairs = new List<(TcpClient, TcpClient)>();
        try
        {
            List<Avalon.World.WorldConnection> connections = AdmitSessions(server, 3, TimeProvider.System, pairs);
            await GracefulShutdownHelper.NotifyAndCloseAsync(connections[0], "kicked", DisconnectReason.Kicked);
            await connections[1].CloseAsync(false); // the peer closed, or the connection was lost

            await server.Stop().WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(new Dictionary<long, string>
            {
                [1] = GameSessionEndReasons.Kicked,
                [2] = GameSessionEndReasons.Drop,
                [3] = GameSessionEndReasons.Shutdown,
            }, reasons);
        }
        finally { Dispose(pairs); }
    }

    /// <summary>One heartbeat through the tick's lease check: asked for, answered off the tick, applied by the next check.</summary>
    private static async Task HeartbeatAsync(Avalon.World.WorldConnection connection, TimeProvider clock)
    {
        connection.RequestGameplayRevalidation();
        connection.AdvanceGameplayLease(TickTime.Read(clock));
        var heartbeat = (Task)typeof(Avalon.World.WorldConnection)
            .GetField("_heartbeat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;
        await heartbeat.WaitAsync(TimeSpan.FromSeconds(30));
        connection.AdvanceGameplayLease(TickTime.Read(clock));
    }

    /// <summary>Identity's renewal of <paramref name="lease"/>: 45 seconds from now, on the test's clock.</summary>
    private static SessionLeaseResponse Renewal(GameSessionLease lease, TimeProvider clock) => new()
    {
        State = "active",
        AccountId = lease.Authority.AccountId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        GameSessionId = lease.Authority.GameSessionId.ToString("D"),
        GameContextId = lease.GameContextId.ToString("D"),
        FencingToken = lease.Authority.FencingToken.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ServerId = lease.ServerId,
        WorldId = lease.WorldId,
        AccessLevel = (ushort)lease.AccessLevel,
        CredentialsVersion = 0,
        SessionEpoch = "0",
        LeaseUntil = clock.GetUtcNow().UtcDateTime.AddSeconds(45),
        AuthorizationUntil = clock.GetUtcNow().UtcDateTime.AddMinutes(5),
    };

    private static List<Avalon.World.WorldConnection> AdmitSessions(TestWorldServer server, int count, TimeProvider clock,
        List<(TcpClient, TcpClient)> pairs)
    {
        var connections = new List<Avalon.World.WorldConnection>();
        for (int i = 0; i < count; i++)
        {
            (TcpClient client, TcpClient serverSide) = CreateLoopbackPair();
            pairs.Add((client, serverSide));
            var connection = new Avalon.World.WorldConnection(server, client, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
            server.Add(connection);
            GameplayTestAdmission.Admit(connection, new AccountId(i + 1), clock);
            connections.Add(connection);
        }

        return connections;
    }

    private static void Dispose(List<(TcpClient, TcpClient)> pairs)
    {
        foreach ((TcpClient client, TcpClient serverSide) in pairs)
        {
            client.Dispose();
            serverSide.Dispose();
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen = Volatile.Read(ref target);
        while (value > seen)
        {
            int previous = Interlocked.CompareExchange(ref target, value, seen);
            if (previous == seen) return;
            seen = previous;
        }
    }

    /// <summary>The bound the drain is given, counting the ends handed to it.</summary>
    private sealed class CountingWork(IWorldDatabaseWork inner, int expected) : IWorldDatabaseWork
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _submitted;

        public Task AllSubmitted => _all.Task;

        public Task<T> Run<T>(Func<Task<T>> operation)
        {
            if (Interlocked.Increment(ref _submitted) == expected) _all.TrySetResult();
            return inner.Run(operation);
        }
    }

    private Avalon.World.WorldConnection Connect(TestWorldServer server)
    {
        var connection = new Avalon.World.WorldConnection(
            server, _clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>());
        server.Add(connection);
        return connection;
    }

    private static (TcpClient clientSide, TcpClient serverSide) CreateLoopbackPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, port);
        TcpClient serverSide = listener.AcceptTcpClient();
        listener.Stop();
        return (clientSide, serverSide);
    }

    /// <summary>Reaches the two members the shutdown path needs: the connection set, and the stop itself.</summary>
    private sealed class TestWorldServer : WorldServer
    {
        public TestWorldServer(IWorld world, ICharacterSaver? saver = null, IGameAdmissionClient? admission = null,
            TradeService? trades = null) : base(
            Substitute.For<IPacketManager>(),
            NullLoggerFactory.Instance,
            new AnyServiceProvider(admission),
            Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = 0 }),
            world,
            Substitute.For<IScriptManager>(),
            Substitute.For<IReplicatedCache>(),
            Substitute.For<IScriptHotReloader>(),
            saver ?? new CharacterSaver(Substitute.For<ICharacterSaveRepository>(), NullLogger<CharacterSaver>.Instance),
            new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System, NullLogger<PartyService>.Instance),
            trades: trades)
        { }

        public void Add(Avalon.World.WorldConnection connection) => AddConnection(connection);

        public Task Stop(CancellationToken stoppingToken = default) => OnStoppingAsync(stoppingToken);
    }

    /// <summary>
    /// The world server reflects over every packet handler in the assembly and activates each one,
    /// so standing it up needs a container that answers for all of their dependencies rather than a
    /// list of the ones this test cares about.
    /// </summary>
    private sealed class AnyServiceProvider(IGameAdmissionClient? admission = null) : IServiceProvider
    {
        private QuestService? _quests;
        private Avalon.World.Trading.TradeService? _trades;
        private Avalon.World.Items.ItemUseService? _itemUses;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ILoggerFactory)) return NullLoggerFactory.Instance;

            // The game settings, the defaults: the input handler reads its movement budget (#1267) from them.
            if (serviceType == typeof(IOptions<GameConfiguration>)) return Options.Create(new GameConfiguration());

            // The one admission client the drain ends sessions through, when a test watches it.
            if (serviceType == typeof(IGameAdmissionClient) && admission is not null) return admission;

            // PvpToggleHandler takes the one PvP toggle (#164), a class with settings and a clock.
            if (serviceType == typeof(PvpToggle))
                return new PvpToggle(Options.Create(new GameConfiguration()), TimeProvider.System);

            // The party handlers (2026-09-30) take the one party service, a class with settings, a clock and a logger.
            // The chat handler (#722) takes the one chat limiter, a class with settings and a clock; off, as the defaults are.
            if (serviceType == typeof(Avalon.World.Chat.ChatRateLimiter))
                return new Avalon.World.Chat.ChatRateLimiter(Options.Create(new GameConfiguration()), TimeProvider.System);

            // The trade handlers (#1175) take the one trade service.
            if (serviceType == typeof(Avalon.World.Trading.TradeService))
                return _trades ??= Avalon.Server.World.UnitTests.Trading.TradeTestWorld.InertService();

            if (serviceType == typeof(PartyService))
            {
                return new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System,
                    NullLogger<PartyService>.Instance);
            }

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

            // Handlers also take plain settings objects; a default one is enough to construct them.
            return serviceType.GetConstructor(Type.EmptyTypes) is not null
                ? Activator.CreateInstance(serviceType)
                : null;
        }
    }
}
