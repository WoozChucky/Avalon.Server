using System.Net;
using System.Net.Sockets;
using Avalon.Combat;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Seeding;
using Avalon.Domain.Auth;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Maintenance;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Avalon.World.Pvp;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// The barrier only bounds anything because the tick sweeps it. A policy nothing calls releases
/// nobody, and every login is on that path until a client sends CMSG_CHARACTER_LOADED. The same
/// holds for the per-tick inventory update: the flusher only reaches a client because the tick
/// calls it, and for the tick time the ping carries, which only the tick measures.
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
    public void Time_the_barrier_on_the_container_clock()
    {
        // Pending since now by the wall clock, an hour past the barrier by the container's (#820).
        var clock = new Avalon.Server.World.UnitTests.Loot.FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build(clock: clock);
        IMapInstance instance = Substitute.For<IMapInstance>();
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), instance, DateTime.UtcNow.Ticks);

        server.Tick();

        world.Received(1).SpawnInInstance(connection, instance);
    }

    [Fact]
    public async Task Timeout_release_checks_maintenance_once_and_never_spawns_a_refused_character()
    {
        IWorldEntryGate gate = Substitute.For<IWorldEntryGate>();
        gate.CheckAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(default(WorldEntryDecision));
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build(gate: gate);
        connection.AccountId = new AccountId(42);
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), Substitute.For<IMapInstance>(),
            DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(16).Ticks);

        await TickUntil(server, () => connection.IsClosing || connection.PendingSpawn is null);

        Assert.True(connection.IsClosing);
        world.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
        await gate.Received(1).CheckAsync(new AccountId(42), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Timeout_release_spawns_after_the_entry_check_allows_it()
    {
        IWorldEntryGate gate = Substitute.For<IWorldEntryGate>();
        gate.CheckAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntryDecision(true, DateTime.MaxValue));
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build(gate: gate);
        connection.AccountId = new AccountId(42);
        IMapInstance instance = Substitute.For<IMapInstance>();
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), instance,
            DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(16).Ticks);

        await TickUntil(server, () => connection.IsClosing || connection.PendingSpawn is null);

        Assert.Null(connection.PendingSpawn);
        world.Received(1).SpawnInInstance(connection, instance);
        await gate.Received(1).CheckAsync(new AccountId(42), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An entry decision taken before a cutoff is judged again as the spawn runs (<c>RunIfEntryAllowed</c>). A
    /// maintenance cutoff closes a non-Admin's connection itself, on the tick, before the decision reaches it, so the
    /// cutoff here is the restart drain's (#768): at its deadline it blocks the connection and leaves the close to the
    /// stop, and only the check at the spawn keeps the character out. The decision allowed entry, and ended before the
    /// deadline came.
    /// </summary>
    [Fact]
    public async Task Do_not_spawn_when_the_deadline_applies_after_a_completed_entry_check()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        IWorldEntryGate gate = Substitute.For<IWorldEntryGate>();
        var check = new TaskCompletionSource<WorldEntryDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.CheckAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            asked.TrySetResult();
            return check.Task;
        });
        var drainTime = TimeSpan.FromSeconds(30);
        var coordinator = new WorldMaintenanceCoordinator(new WorldId(1),
            Substitute.For<IWorldMaintenanceRepository>(), Substitute.For<ICharacterSaver>(),
            clock, NullLogger<WorldMaintenanceCoordinator>.Instance,
            Microsoft.Extensions.Options.Options.Create(
                new Avalon.World.Configuration.WorldShutdownConfiguration { DrainTime = drainTime }));
        coordinator.ApplyCommitted(new WorldMaintenanceState(false, 1, null));
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) =
            Build(gate: gate, coordinator: coordinator, clock: clock);
        connection.AccountId = new AccountId(42);
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), Substitute.For<IMapInstance>(),
            clock.GetUtcNow().UtcTicks - TimeSpan.FromSeconds(16).Ticks);
        Task drain = coordinator.DrainForRestartAsync(CancellationToken.None);

        server.Tick(); // takes the drain, and asks the check
        await asked.Task.WaitAsync(s_hang);
        check.SetResult(new WorldEntryDecision(true, DateTime.MaxValue));
        clock.Advance(drainTime);
        await TickUntil(server, () => connection.IsClosing || connection.PendingSpawn is null);

        Assert.True(connection.IsClosing);
        world.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
        await drain.WaitAsync(s_hang); // the tick ended the drain at its deadline
    }

    [Fact]
    public async Task Spawn_during_countdown_after_a_completed_entry_check()
    {
        IWorldEntryGate gate = Substitute.For<IWorldEntryGate>();
        gate.CheckAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntryDecision(true, DateTime.UtcNow.AddMinutes(1)));
        var coordinator = new WorldMaintenanceCoordinator(new WorldId(1),
            Substitute.For<IWorldMaintenanceRepository>(), Substitute.For<ICharacterSaver>(),
            TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance,
            Microsoft.Extensions.Options.Options.Create(new Avalon.World.Configuration.WorldShutdownConfiguration()));
        coordinator.ApplyCommitted(new WorldMaintenanceState(true, 2, DateTime.UtcNow.AddMinutes(1)));
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build(gate: gate, coordinator: coordinator);
        connection.AccountId = new AccountId(42);
        IMapInstance instance = Substitute.For<IMapInstance>();
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), instance,
            DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(16).Ticks);

        await TickUntil(server, () => connection.IsClosing || connection.PendingSpawn is null);

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
            TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance,
            Microsoft.Extensions.Options.Options.Create(new Avalon.World.Configuration.WorldShutdownConfiguration()));
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
    public void Run_the_maintenance_cutoff_on_the_container_clock()
    {
        // The container's clock is an hour ahead of the wall clock, and the deadline lies between the two: only a
        // tick that reads the container's clock reaches it.
        var clock = new Avalon.Server.World.UnitTests.Loot.FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        var coordinator = new WorldMaintenanceCoordinator(new WorldId(1),
            Substitute.For<IWorldMaintenanceRepository>(), Substitute.For<ICharacterSaver>(),
            clock, NullLogger<WorldMaintenanceCoordinator>.Instance,
            Microsoft.Extensions.Options.Options.Create(new Avalon.World.Configuration.WorldShutdownConfiguration()));
        (TestWorldServer server, _, Avalon.World.WorldConnection connection) =
            Build(coordinator: coordinator, clock: clock);
        connection.AccountId = new AccountId(42);
        coordinator.Offer(new WorldMaintenanceState(true, 1, DateTime.UtcNow.AddMinutes(30)));

        server.Tick();

        Assert.True(connection.IsClosing);
    }

    [Fact]
    public async Task Judge_an_entry_decision_on_the_container_clock_without_a_coordinator()
    {
        // Valid by the wall clock, expired by the container's: the release must refuse it.
        var clock = new Avalon.Server.World.UnitTests.Loot.FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        IWorldEntryGate gate = Substitute.For<IWorldEntryGate>();
        gate.CheckAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntryDecision(true, DateTime.UtcNow.AddMinutes(30)));
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) = Build(gate: gate, clock: clock);
        connection.AccountId = new AccountId(42);
        connection.SetPendingSpawn(PendingSpawnConnection.Character(), Substitute.For<IMapInstance>(),
            DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(16).Ticks);

        await TickUntil(server, () => connection.IsClosing || connection.PendingSpawn is null);

        Assert.True(connection.IsClosing);
        world.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
    }

    [Fact]
    public void Refuse_a_completed_entry_decision_after_its_five_second_lifetime()
    {
        var coordinator = new WorldMaintenanceCoordinator(new WorldId(1),
            Substitute.For<IWorldMaintenanceRepository>(), Substitute.For<ICharacterSaver>(),
            TimeProvider.System, NullLogger<WorldMaintenanceCoordinator>.Instance,
            Microsoft.Extensions.Options.Options.Create(new Avalon.World.Configuration.WorldShutdownConfiguration()));
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

        CharacterEntity character = New();
        character.ApplyStats(new DerivedCharacterStats(MaxHealth: 240, MaxPower: 100, Stamina: 22, Strength: 23,
            Agility: 20, Intellect: 20, Armor: 0, BlockPct: 0f, Evasion: 0, CritPct: 80f, AttackDamage: 46,
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
    /// The time-sync ping carries the world's mean tick time (#1016), measured on every tick from the tick's own start
    /// and end, as the client reads it off the wire. The first tick's ping (the start burst's, asked for before it) is
    /// queued before any window of about a second has closed, so it reads 0, "not reported". That tick began a second
    /// ago and so closes the first window, one tick long; the next ping carries its mean.
    /// </summary>
    [Fact]
    public async Task Send_the_worlds_mean_tick_time_with_the_ping_and_0_before_a_window_closed()
    {
        (TestWorldServer server, _, Avalon.World.WorldConnection connection) = Build();
        var wire = new MemoryStream();
        connection.InitOutboxForTest(new PacketStream(wire));

        // Each ping is written by the send thread before the next is queued: two pings still queued go out as one.
        connection.RequestInitialTimeSyncPing();
        server.Tick(tickStart: System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency);
        server.SendScheduler.RunAllPasses();
        connection.SendTimeSyncPing(); // the next ping, as the tick queues it
        server.SendScheduler.RunAllPasses();

        List<uint> pings = (await PingsWritten(wire)).ConvertAll(p => p.TickMicros);

        Assert.Equal(2, pings.Count);
        Assert.Equal(0u, pings[0]);
        Assert.InRange(pings[1], 1_000_000u, 10_000_000u); // the one tick of the window: a second, and its own work
    }

    /// <summary>
    /// The time-sync ping names the world's tick beside its server time (#1066), both read by the send thread as it
    /// stamps the ping, so the pair is one instant a client can anchor the state broadcast's ticks to; and the rate the
    /// loop runs at. Queued by the first tick (tick 0, the start burst's first ping) and written only after three ticks
    /// and 50 ms of the clock, it carries the count and the time of its writing, not those it was queued at: tick 3, the
    /// count the next broadcast names, and the clock 50 ms on.
    /// </summary>
    [Fact]
    public async Task Stamp_the_ping_with_the_world_tick_and_the_server_time_it_is_written_at()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        (TestWorldServer server, _, Avalon.World.WorldConnection connection) = Build(clock: clock);
        var wire = new MemoryStream();
        connection.InitOutboxForTest(new PacketStream(wire));

        connection.RequestInitialTimeSyncPing();
        server.Tick();
        server.Tick();
        server.Tick();
        clock.Advance(TimeSpan.FromMilliseconds(50));
        server.SendScheduler.RunAllPasses();

        SPingPacket ping = Assert.Single(await PingsWritten(wire));
        Assert.Equal(3, server.CompletedTicks);
        Assert.Equal(3, ping.WorldTick);
        Assert.Equal(clock.GetUtcNow().UtcTicks, ping.ServerTimestamp);
        Assert.Equal(60u, ping.WorldTickRate);
    }

    /// <summary>Every time-sync ping written to <paramref name="wire" />, decoded, oldest first.</summary>
    private static async Task<List<SPingPacket>> PingsWritten(MemoryStream wire)
    {
        var pings = new List<SPingPacket>();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await foreach (ReadOnlyMemory<byte> raw in new PacketStream(new MemoryStream(wire.ToArray()))
            .EnumerateRawFramesAsync(256, int.MaxValue, guard.Token))
        {
            var frame = InboundPacketFrame.ParseFrame(raw);
            if (frame.Header.Type == NetworkPacketType.SMSG_PING)
                pings.Add(ProtoBuf.Serializer.Deserialize<SPingPacket>(frame.Payload));
        }

        return pings;
    }

    /// <summary>
    /// The time-sync cadence (#1034): a connection's first ping is the start burst, three in the two seconds after its
    /// handshake (on the next tick, then twice more 60 ticks apart), so its client has a measured clock offset in about
    /// two seconds; before it, its turn on its slot sends nothing, since no pong could be let in. The burst hands over to
    /// the slot, where it is pinged every 300 ticks (five seconds at 60 Hz). Its turns follow the connection, not its
    /// place in the list: the two connections ahead of it are removed along the way, which moved it from third to first,
    /// and it is still pinged on every tick it is due, and only then.
    /// </summary>
    [Fact]
    public async Task Ping_every_five_seconds_on_its_slot_and_three_times_after_the_handshake_whoever_leaves_ahead()
    {
        (TestWorldServer server, _, Avalon.World.WorldConnection first) = Build();
        using var second = Avalon.Server.World.UnitTests.GameAuth.WorldAdmissionConnection.Create(server, tls: false);
        using var pinged = Avalon.Server.World.UnitTests.GameAuth.WorldAdmissionConnection.Create(server, tls: false);
        // Not past TLS, so the admission timeout leaves them alone: the time sync pings a connection in any state.
        server.Add(second);
        server.Add(pinged);
        var wire = new MemoryStream();
        pinged.InitOutboxForTest(new PacketStream(wire));
        int slot = pinged.TimeSyncSlot;

        var ticks = new List<int>();
        int written = 0;
        for (int tick = 0; tick <= slot + 2 * WorldServer.TimeSyncTicksPeriod + 10; tick++)
        {
            if (tick == slot + 50)
            {
                await server.RemoveConnection(first);
                await server.RemoveConnection(second);
            }

            if (tick == slot + 100)
                pinged.RequestInitialTimeSyncPing();
            server.Tick();
            server.SendScheduler.RunAllPasses();

            byte[] bytes = wire.ToArray();
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await foreach (ReadOnlyMemory<byte> raw in new PacketStream(new MemoryStream(bytes, written, bytes.Length - written))
                .EnumerateRawFramesAsync(256, int.MaxValue, guard.Token))
            {
                if (InboundPacketFrame.ParseFrame(raw).Header.Type == NetworkPacketType.SMSG_PING)
                    ticks.Add(tick);
            }

            written = bytes.Length;
        }

        Assert.Equal([slot + 100, slot + 160, slot + 220, slot + 300, slot + 600], ticks);
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
        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.TemplateId.Returns(new Avalon.Common.ValueObjects.MapTemplateId(1));
        Dictionary<Avalon.Common.ObjectGuid, ICharacter> roster = new() { [nym.Guid] = nym };
        instance.Characters.Returns(roster);
        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.ActiveInstances.Returns([instance]);
        world.InstanceRegistry.Returns(registry);

        server.Tick();
        Assert.Equal((ushort)4, presence.Take()?.WorldId);

        server.Tick();
        Assert.Null(presence.Take());
    }

    /// <summary>
    /// The lease check (#977): the tick ends gameplay on the first tick its lease is within
    /// <see cref="GameAuthPolicy.SaveDrainMargin" /> of its end, by whichever clock gets there first, the wall clock
    /// (its <c>LeaseUntil</c>) or the monotonic one (the time granted, counted from the grant), and not a tick before.
    /// The passes judge the lease at the time the check was made, so a session pass that runs past the margin (here a
    /// handler that takes 6 s) has every lease checked again before the map pass: one that came due meanwhile ends
    /// there, and the map pass finds its connection closing.
    /// </summary>
    [Theory]
    [InlineData("wall clock")]
    [InlineData("monotonic clock")]
    [InlineData("long session pass")]
    public void End_gameplay_on_the_tick_the_lease_comes_within_the_save_margin_of_its_end(string reaches)
    {
        var clock = new SplitClock();
        (TestWorldServer server, IWorld world, Avalon.World.WorldConnection built) = Build(clock: clock);
        // A connection a packet can be delivered to, for the long pass; the built one for the others.
        using Avalon.Server.World.UnitTests.GameAuth.WorldAdmissionConnection? deliverable = reaches == "long session pass"
            ? Avalon.Server.World.UnitTests.GameAuth.WorldAdmissionConnection.Create(server, clock: clock)
            : null;
        Avalon.World.WorldConnection connection = built;
        if (deliverable is not null)
        {
            server.Add(deliverable);
            connection = deliverable;
        }

        Avalon.World.GameAuth.GameSessionLease lease = Avalon.Server.World.UnitTests.GameAuth.GameplayTestAdmission.Admit(
            connection, clock: clock); // 44 s
        TimeSpan due = TimeSpan.FromSeconds(44) - GameAuthPolicy.SaveDrainMargin;

        if (reaches == "long session pass")
        {
            clock.Move(due - TimeSpan.FromSeconds(1), wallClock: true);
            server.Tick();
            Assert.True(connection.IsGameplayAuthorized);

            // A session packet whose handler takes 6 s of both clocks, past the margin and past the lease's due time.
            server.PacketHandlers[NetworkPacketType.CMSG_CHARACTER_LIST] = new Slow(() =>
            {
                clock.Move(TimeSpan.FromSeconds(6), wallClock: true);
                clock.Move(TimeSpan.FromSeconds(6), wallClock: false);
            });
            bool? closingAtMapPass = null;
            world.When(w => w.Update(Arg.Any<TimeSpan>())).Do(_ => closingAtMapPass = connection.IsClosing);
            deliverable!.Deliver(
                NetworkPacketType.CMSG_CHARACTER_LIST, new CCharacterListPacket());

            server.Tick();

            Assert.True(closingAtMapPass);
            Assert.False(lease.IsActive);
            return;
        }

        bool wallClock = reaches == "wall clock";
        clock.Move(due - TimeSpan.FromTicks(1), wallClock);
        server.Tick();
        Assert.True(connection.IsGameplayAuthorized);
        Assert.False(connection.IsClosing);

        clock.Move(TimeSpan.FromTicks(1), wallClock);
        server.Tick();
        Assert.False(lease.IsActive);
        Assert.False(connection.IsGameplayAuthorized);
        Assert.True(connection.IsClosing);
    }

    /// <summary>A handler that takes as long as <paramref name="takeTime" /> moves the clock.</summary>
    private sealed class Slow(Action takeTime) : IWorldPacketHandler
    {
        public void Execute(IWorldConnection connection, Avalon.Network.Packets.Packet packet) => takeTime();
    }

    /// <summary>A clock whose wall and monotonic readings move apart, as a wall clock that is set does.</summary>
    private sealed class SplitClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp = 1_000;

        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Move(TimeSpan by, bool wallClock)
        {
            if (wallClock)
                _now += by;
            else
                _timestamp += by.Ticks;
        }
    }

    /// <summary>A limit on a hang only: what the tests here wait for needs a thread-pool worker, not time (#960).</summary>
    private static readonly TimeSpan s_hang = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Ticks until <paramref name="settled" /> holds. A release asks the entry check on the thread pool, off the tick,
    /// and takes its decision on the first tick after it ends, whenever a worker gets to it: a test run can hold every
    /// worker for seconds (#960). So this waits for the outcome itself, never for an amount of time (#1019).
    /// </summary>
    private static async Task TickUntil(TestWorldServer server, Func<bool> settled)
    {
        using var hang = new CancellationTokenSource(s_hang);
        while (!settled())
        {
            server.Tick();
            await Task.Delay(10, hang.Token);
        }
    }

    /// <summary>Reference data with the seeded combat formula, which the tick's sheet flush reads (#506).</summary>
    private static readonly Lazy<StaticData> s_seededData = new(() => TestStaticData.LoadAsync().GetAwaiter().GetResult());

    private (TestWorldServer server, IWorld world, Avalon.World.WorldConnection connection) Build(PartyService? parties = null,
        Avalon.World.Presence.PresenceCapture? presence = null, IWorldEntryGate? gate = null,
        WorldMaintenanceCoordinator? coordinator = null, TimeProvider? clock = null)
    {
        StaticData data = s_seededData.Value;   // loaded outside Returns, which it would otherwise interrupt
        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration { CharacterLoadTimeoutSeconds = 15 });
        world.Data.Returns(data);

        var server = new TestWorldServer(world, parties, presence, gate, coordinator, clock);
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
            WorldMaintenanceCoordinator? coordinator = null, TimeProvider? clock = null) : base(
            Substitute.For<IPacketManager>(),
            NullLoggerFactory.Instance,
            new AnyServiceProvider(presence, clock),
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

        public void Tick(long tickStart = 0) => Update(TimeSpan.FromMilliseconds(16), tickStart);
    }

    /// <summary>
    /// The world server reflects over every packet handler in the assembly and activates each one,
    /// so standing it up needs a container that answers for all of their dependencies.
    /// </summary>
    private sealed class AnyServiceProvider(Avalon.World.Presence.PresenceCapture? presence = null,
        TimeProvider? clock = null) : IServiceProvider
    {
        private QuestService? _quests;
        private Avalon.World.Trading.TradeService? _trades;
        private Avalon.World.Items.ItemUseService? _itemUses;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ILoggerFactory)) return NullLoggerFactory.Instance;

            // The presence capture (#639), only where a test hands one over; production registers it.
            if (serviceType == typeof(Avalon.World.Presence.PresenceCapture)) return presence;

            // The container's clock: the one a test hands over, else the system clock, as production has a real one.
            if (serviceType == typeof(TimeProvider)) return clock ?? TimeProvider.System;

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
            {
                return Activator.CreateInstance(
                    typeof(NullLogger<>).MakeGenericType(serviceType.GenericTypeArguments[0]));
            }

            if (serviceType.IsInterface || serviceType.IsAbstract)
                return Substitute.For([serviceType], []);

            return serviceType.GetConstructor(Type.EmptyTypes) is not null
                ? Activator.CreateInstance(serviceType)
                : null;
        }
    }
}
